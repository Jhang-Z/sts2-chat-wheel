using Godot;
using System.Collections.Generic;

namespace VoiceRoulette.UI;

// Two-ring 16-slot voice wheel:
//   • inner ring (slots 0..7)  — close to center, daily reactions
//   • outer ring (slots 8..15) — pushed further, tactical phrases
//   • per-sector text alignment so all items hug the wheel evenly
//   • center hub with a gold arrow that rotates AND scales — short for
//     inner, long for outer — giving a clear "which ring am I in" cue
//   • inactive ring dims while you hover the active one, so the
//     16 items don't feel like a wall of text.
public sealed partial class WheelUI : CanvasLayer
{
    private const int   SectorCount      = 8;
    private const int   RingCount        = 2;
    private const int   SlotCount        = SectorCount * RingCount;  // 16

    // Geometry — both rings sit symmetrically around the hub.
    private const float InnerTextRadius  = 130f;
    private const float OuterTextRadius  = 240f;
    private const float TextW            = 280f;
    private const float TextH            = 40f;
    private const float HubRadius        = 42f;
    private const float HubInnerRadius   = 36f;

    // Mouse-distance thresholds for ring selection. Tweaked for 1080p+
    // displays — feels natural with a moderate flick.
    private const float MouseDeadZoneSq  = 36f * 36f;     // 36px deadzone
    private const float InnerOuterBound  = 170f;          // <170px = inner, ≥170px = outer

    // Arrow geometry — separate "tip" radius per ring so the arrow visibly
    // points further when you've reached the outer ring.
    private const float InnerArrowTipR   = 60f;
    private const float InnerArrowBaseR  = 46f;
    private const float OuterArrowTipR   = 165f;
    private const float OuterArrowBaseR  = 150f;

    private const int   MaxSectorChars   = 10;

    private static readonly Color HubBg          = new("11100EE0");
    private static readonly Color HubBorder      = StsTheme.MenuAccent;
    private static readonly Color HubDot         = StsTheme.MenuAccent;
    private static readonly Color TextColor      = StsTheme.MenuText;
    private static readonly Color TextColorHover = StsTheme.MenuAccent;
    private static readonly Color TextColorDim   = new("8A7E6260");  // dimmed inactive ring
    private static readonly Color TextOutline    = new("00000099");

    private readonly Sector[] _sectors = new Sector[SlotCount];
    private Polygon2D? _selectionArrow;
    private Vector2 _center;

    private int _selected = -1;
    private List<string> _texts = new();
    private List<bool> _hasVoice = new();
    private bool _initialized;

    public int SelectedIndex => _selected;

    public void Initialize(string modDir, System.Func<string, bool>? hasAudio = null)
    {
        _ = modDir; _ = hasAudio;
        if (_initialized) return;
        _initialized = true;
        Layer = 200;

        var viewport = GetViewport().GetVisibleRect().Size;
        _center = viewport / 2f;

        BuildSectors();
        BuildHub();

        Visible = false;
        GD.Print($"[VR][Wheel] Initialize done. center={_center} slots={SlotCount}");
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    private void BuildSectors()
    {
        for (var ring = 0; ring < RingCount; ring++)
        {
            var radius = ring == 0 ? InnerTextRadius : OuterTextRadius;
            BuildRing(ring, radius);
        }
    }

    private void BuildRing(int ring, float radius)
    {
        var step = Mathf.Tau / SectorCount;
        for (var s = 0; s < SectorCount; s++)
        {
            var angle = -Mathf.Pi / 2f + s * step;
            var anchor = _center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Per-sector alignment so all 8 items hug the ring evenly:
            //   sectors at top (0) / bottom (4)              → centered
            //   right side (1, 2, 3)  → left-aligned, anchor at LEFT edge
            //   left side  (5, 6, 7)  → right-aligned, anchor at RIGHT edge
            HorizontalAlignment ha;
            Vector2 labelPos;
            if (s == 0 || s == 4)
            {
                ha = HorizontalAlignment.Center;
                labelPos = anchor - new Vector2(TextW / 2, TextH / 2);
            }
            else if (s >= 1 && s <= 3)
            {
                ha = HorizontalAlignment.Left;
                labelPos = anchor - new Vector2(0, TextH / 2);
            }
            else
            {
                ha = HorizontalAlignment.Right;
                labelPos = anchor - new Vector2(TextW, TextH / 2);
            }

            var lbl = new Label
            {
                HorizontalAlignment = ha,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(TextW, TextH),
                Position = labelPos,
                ClipText = true,
            };
            lbl.AddThemeColorOverride("font_color", TextColor);
            lbl.AddThemeColorOverride("font_outline_color", TextOutline);
            lbl.AddThemeConstantOverride("outline_size", 6);
            // Inner ring slightly larger font to emphasize "primary" usage.
            var size = ring == 0 ? StsTheme.FontH2 : StsTheme.FontBody;
            lbl.AddThemeFontSizeOverride("font_size", size);
            StsFonts.ApplyTo(lbl, StsFonts.FontWeight.Bold);
            AddChild(lbl);

            _sectors[ring * SectorCount + s] = new Sector { TextLabel = lbl, Ring = ring, SectorIndex = s };
        }
    }

    private void BuildHub()
    {
        AddChild(MakeDisc(_center, HubRadius, HubBg, 64));
        AddChild(MakeRing(_center, HubRadius, HubBorder, 2.5f, 64));
        AddChild(MakeRing(_center, HubInnerRadius, HubBorder, 1f, 64));
        AddChild(MakeDisc(_center, 3.5f, HubDot, 24));

        // Arrow polygon will be reshaped per-ring inside Highlight(); we
        // instantiate it once with a placeholder shape.
        _selectionArrow = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new(0, -InnerArrowTipR),
                new(-9, -InnerArrowBaseR),
                new(9, -InnerArrowBaseR),
            },
            Color = HubDot,
            Position = _center,
            Visible = false,
        };
        AddChild(_selectionArrow);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public void OpenWheel(IList<string> lineTexts, IList<bool> hasVoice)
    {
        if (!_initialized) return;
        _selected = -1;
        _texts = new List<string>(lineTexts);
        _hasVoice = new List<bool>(hasVoice);
        Visible = true;
        ApplyAll();
    }

    public void CloseWheel()
    {
        _selected = -1;
        Visible = false;
    }

    /// <summary>
    /// Mouse position relative to the wheel center decides slot:
    ///   distance &lt; deadzone → none
    ///   distance &lt; bound    → inner ring (slot 0-7)
    ///   distance ≥ bound       → outer ring (slot 8-15)
    /// angle picks the sector within the chosen ring.
    /// </summary>
    public void SetSelectedFromMouse(Vector2 mouseDelta)
    {
        if (mouseDelta.LengthSquared() < MouseDeadZoneSq)
        {
            Highlight(-1);
            return;
        }
        var ring = mouseDelta.Length() < InnerOuterBound ? 0 : 1;

        var step = Mathf.Tau / SectorCount;
        var angle = Mathf.Atan2(mouseDelta.Y, mouseDelta.X) + Mathf.Pi / 2f + step / 2f;
        if (angle < 0) angle += Mathf.Tau;
        var sector = (int)(angle / step) % SectorCount;
        Highlight(ring * SectorCount + sector);
    }

    // -------------------------------------------------------------------------
    // Visual state
    // -------------------------------------------------------------------------

    private void ApplyAll()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            var text = i < _texts.Count ? _texts[i] : "";
            var voice = i < _hasVoice.Count && _hasVoice[i];
            ApplySector(i, text, voice, hovered: false);
        }
        if (_selectionArrow != null) _selectionArrow.Visible = false;
    }

    private void ApplySector(int slot, string text, bool voice, bool hovered)
    {
        var s = _sectors[slot];
        var icon = string.IsNullOrEmpty(text) ? "" : (voice ? "◀)) " : "");
        s.TextLabel.Text = icon + Truncate(text, MaxSectorChars);

        // Ring-aware coloring: hovered = gold; same ring as hovered = normal;
        // other ring = dim. This makes "which ring am I on" obvious without
        // adding extra widgets.
        var hoveredRing = _selected >= 0 ? _selected / SectorCount : -1;
        Color color;
        if (hovered) color = TextColorHover;
        else if (hoveredRing == -1 || s.Ring == hoveredRing) color = TextColor;
        else color = TextColorDim;
        s.TextLabel.AddThemeColorOverride("font_color", color);

        // Inner ring naturally a touch bigger; bump the hovered slot a step up.
        var size = s.Ring == 0
            ? (hovered ? StsTheme.FontH1 : StsTheme.FontH2)
            : (hovered ? StsTheme.FontH2 : StsTheme.FontBody);
        s.TextLabel.AddThemeFontSizeOverride("font_size", size);
    }

    private void Highlight(int idx)
    {
        if (idx == _selected) return;

        var prev = _selected;
        _selected = idx;

        // Repaint everyone — coloring depends on _selected (ring dimming).
        for (var i = 0; i < SlotCount; i++)
        {
            var t = i < _texts.Count ? _texts[i] : "";
            var v = i < _hasVoice.Count && _hasVoice[i];
            ApplySector(i, t, v, hovered: i == idx);
        }

        if (idx >= 0 && idx < SlotCount)
        {
            // Rotate + reshape arrow to indicate ring + sector.
            if (_selectionArrow != null)
            {
                var ring = idx / SectorCount;
                var sector = idx % SectorCount;
                var step = Mathf.Tau / SectorCount;
                var angle = -Mathf.Pi / 2f + sector * step;
                _selectionArrow.Rotation = angle + Mathf.Pi / 2f;

                var tipR  = ring == 0 ? InnerArrowTipR  : OuterArrowTipR;
                var baseR = ring == 0 ? InnerArrowBaseR : OuterArrowBaseR;
                _selectionArrow.Polygon = new Vector2[]
                {
                    new(0, -tipR), new(-9, -baseR), new(9, -baseR),
                };
                _selectionArrow.Visible = true;
            }
        }
        else
        {
            if (_selectionArrow != null) _selectionArrow.Visible = false;
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }

    // -------------------------------------------------------------------------
    // Geometry helpers
    // -------------------------------------------------------------------------

    private static Polygon2D MakeDisc(Vector2 c, float r, Color color, int segs)
    {
        var pts = new Vector2[segs];
        for (var i = 0; i < segs; i++)
        {
            var a = i * Mathf.Tau / segs;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return new Polygon2D { Polygon = pts, Color = color };
    }

    private static Line2D MakeRing(Vector2 c, float r, Color color, float width, int segs)
    {
        var line = new Line2D
        {
            DefaultColor = color, Width = width, Antialiased = true, Closed = true,
        };
        for (var i = 0; i < segs; i++)
        {
            var a = i * Mathf.Tau / segs;
            line.AddPoint(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
        return line;
    }

    private sealed class Sector
    {
        public required Label TextLabel;
        public required int Ring;
        public required int SectorIndex;
    }
}
