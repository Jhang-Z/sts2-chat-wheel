using Godot;
using System.Collections.Generic;

namespace VoiceRoulette.UI;

// Minimalist 8-slot voice wheel — Dota-style:
//   • No backdrop, no duplicate label below — just the 8 text items
//   • Per-sector alignment: left-side texts are right-aligned (grow away from
//     center), right-side texts are left-aligned, top/bottom centered.
//     This keeps the 8 items visually equidistant from the hub regardless of
//     text length.
//   • Bigger round center hub with a gold arrow that rotates to point at the
//     currently-hovered sector (matches the user's reference image).
//   • Selected sector → text becomes gold + bold + slightly larger.
public sealed partial class WheelUI : CanvasLayer
{
    private const int   SectorCount    = 8;
    private const float TextRadius     = 160f;  // center → text anchor (closer to hub)
    private const float TextW          = 280f;
    private const float TextH          = 40f;
    private const float HubRadius      = 42f;
    private const float HubInnerRadius = 36f;   // double-ring ornament radius
    private const float ArrowTipR      = 60f;   // OUTSIDE the hub, between hub and text
    private const float ArrowBaseR     = 46f;
    private const int   MaxSectorChars = 10;
    private const float MouseDeadZoneSq = 36f;

    private static readonly Color HubBg          = new("11100EE0");
    private static readonly Color HubBorder      = StsTheme.MenuAccent;
    private static readonly Color HubDot         = StsTheme.MenuAccent;
    private static readonly Color TextColor      = StsTheme.MenuText;
    private static readonly Color TextColorHover = StsTheme.MenuAccent;
    private static readonly Color TextOutline    = new("00000099");

    private readonly Sector[] _sectors = new Sector[SectorCount];
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
        GD.Print($"[VR][Wheel] Initialize done. center={_center}");
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    private void BuildSectors()
    {
        var step = Mathf.Tau / SectorCount;
        for (var i = 0; i < SectorCount; i++)
        {
            var angle = -Mathf.Pi / 2f + i * step;
            var anchor = _center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * TextRadius;

            // Per-sector alignment so all 8 items hug the wheel evenly:
            //   sectors at top (0) / bottom (4)              → centered
            //   right side (1, 2, 3)  → left-aligned, anchor at LEFT edge
            //   left side  (5, 6, 7)  → right-aligned, anchor at RIGHT edge
            HorizontalAlignment ha;
            Vector2 labelPos;
            if (i == 0 || i == 4)
            {
                ha = HorizontalAlignment.Center;
                labelPos = anchor - new Vector2(TextW / 2, TextH / 2);
            }
            else if (i >= 1 && i <= 3)
            {
                ha = HorizontalAlignment.Left;
                labelPos = anchor - new Vector2(0, TextH / 2);
            }
            else // 5, 6, 7
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
            // Outline keeps text legible against any battle background.
            lbl.AddThemeColorOverride("font_outline_color", TextOutline);
            lbl.AddThemeConstantOverride("outline_size", 6);
            lbl.AddThemeFontSizeOverride("font_size", StsTheme.FontH2);
            StsFonts.ApplyTo(lbl, StsFonts.FontWeight.Bold);
            AddChild(lbl);

            _sectors[i] = new Sector { TextLabel = lbl };
        }
    }

    private void BuildHub()
    {
        // Ornamental double-ring hub (StS2-style):
        //   outer thicker gold ring + inner thinner ring + dim warm fill +
        //   small center dot. No solid black disc — keeps a "compass rose"
        //   feel without obstructing the game scene behind.
        AddChild(MakeDisc(_center, HubRadius, HubBg, 64));
        AddChild(MakeRing(_center, HubRadius, HubBorder, 2.5f, 64));
        AddChild(MakeRing(_center, HubInnerRadius, HubBorder, 1f, 64));
        AddChild(MakeDisc(_center, 3.5f, HubDot, 24));

        // Selection arrow — sits OUTSIDE the hub (between hub and text). When
        // a sector is hovered, the arrow rotates to point at it. Triangle is
        // defined pointing UP (-Y); we rotate by (angle + π/2).
        _selectionArrow = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new( 0,  -ArrowTipR),     // tip (outward, away from center)
                new(-9,  -ArrowBaseR),    // left base
                new( 9,  -ArrowBaseR),    // right base
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

    public void SetSelectedFromMouse(Vector2 mouseDelta)
    {
        if (mouseDelta.LengthSquared() < MouseDeadZoneSq)
        {
            Highlight(-1);
            return;
        }
        var step = Mathf.Tau / SectorCount;
        var angle = Mathf.Atan2(mouseDelta.Y, mouseDelta.X) + Mathf.Pi / 2f + step / 2f;
        if (angle < 0) angle += Mathf.Tau;
        var idx = (int)(angle / step) % SectorCount;
        Highlight(idx);
    }

    // -------------------------------------------------------------------------
    // Visual state
    // -------------------------------------------------------------------------

    private void ApplyAll()
    {
        for (var i = 0; i < SectorCount; i++)
        {
            var text = i < _texts.Count ? _texts[i] : "";
            var voice = i < _hasVoice.Count && _hasVoice[i];
            ApplySector(i, text, voice, hovered: i == _selected);
        }
        if (_selectionArrow != null) _selectionArrow.Visible = false;
    }

    private void ApplySector(int i, string text, bool voice, bool hovered)
    {
        var s = _sectors[i];
        var icon = string.IsNullOrEmpty(text) ? "" : (voice ? "◀)) " : "");
        s.TextLabel.Text = icon + Truncate(text, MaxSectorChars);
        s.TextLabel.AddThemeColorOverride("font_color", hovered ? TextColorHover : TextColor);
        s.TextLabel.AddThemeFontSizeOverride("font_size", hovered ? StsTheme.FontH1 : StsTheme.FontH2);
    }

    private void Highlight(int idx)
    {
        if (idx == _selected) return;

        if (_selected >= 0 && _selected < SectorCount)
        {
            var prev = _selected;
            ApplySector(prev,
                prev < _texts.Count ? _texts[prev] : "",
                prev < _hasVoice.Count && _hasVoice[prev],
                hovered: false);
        }

        _selected = idx;

        if (idx >= 0 && idx < SectorCount)
        {
            ApplySector(idx,
                idx < _texts.Count ? _texts[idx] : "",
                idx < _hasVoice.Count && _hasVoice[idx],
                hovered: true);

            // Rotate the arrow to point at the selected sector.
            if (_selectionArrow != null)
            {
                var step = Mathf.Tau / SectorCount;
                var angle = -Mathf.Pi / 2f + idx * step;
                // Arrow is defined pointing up (-Y); rotate so its tip points
                // outward at `angle`. Atan2's 0 is +X, our arrow up is -Y =
                // angle -π/2, so we rotate by (angle - (-π/2)) = angle + π/2.
                _selectionArrow.Rotation = angle + Mathf.Pi / 2f;
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
    }
}
