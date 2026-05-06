using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace VoiceRoulette.UI;

// Wheel UI faithful to the design mockup. Uses pre-baked PNG textures
// (parchment cards with paper grain, center flame hub, decorative gold ring)
// instead of runtime-drawn polygons. Textures live in <modDir>/textures/ and
// are loaded via ImageTexture.LoadFromFile.
public sealed partial class WheelUI : CanvasLayer
{
    private const int SectorCount = 8;
    private const float SlotRadius = 240f;            // distance from wheel center to slot card center
    private const float CardWidth = 130f;
    private const float CardHeight = 156f;
    private const float CenterHubSize = 130f;
    private const float OuterRingSize = 660f;
    private const float MouseDeadZoneSquared = 25f;

    // Heuristic: pick a glyph based on phrase keywords.
    private static readonly (string keyword, string glyph)[] IconRules =
    {
        ("好",   "★"), ("漂亮", "★"), ("厉害", "★"), ("赞",   "★"),
        ("攻",   "⚔"), ("打",   "⚔"), ("精英", "⚔"),
        ("挡",   "🛡"), ("防",   "🛡"),
        ("撤",   "🏃"), ("快走", "🏃"), ("跑",   "🏃"),
        ("休息", "🔥"), ("回血", "♥"), ("治",   "♥"), ("谢",   "♥"),
        ("等",   "⚠"), ("小心", "⚠"), ("注意", "⚠"),
        ("敌",   "❓"), ("哪",   "❓"), ("?",    "❓"), ("？",   "❓"),
        ("糟",   "💀"), ("死",   "💀"),
        ("继续", "➤"), ("推进", "➤"), ("前",   "➤"),
    };

    private const string IconAudio = "♪";
    private const string IconNoAudio = "○";

    // Texture cache
    private Texture2D? _texCard;
    private Texture2D? _texCardHover;
    private Texture2D? _texCenterHub;
    private Texture2D? _texOuterRing;

    // Per-slot nodes
    private readonly TextureRect[] _slotCards = new TextureRect[SectorCount];
    private readonly Label[] _slotIcons = new Label[SectorCount];
    private readonly Label[] _slotTexts = new Label[SectorCount];
    private readonly Label[] _slotNumbers = new Label[SectorCount];
    private readonly Label[] _slotAudioBadges = new Label[SectorCount];

    // Center cluster
    private Label? _centerLabel;
    private Label? _hintLabel;
    private Line2D? _directionLine;

    // State
    private int _selected = -1;
    private List<string> _texts = new();
    private Func<string, bool>? _hasAudio;
    private string? _modDir;
    private bool _initialized;

    public int SelectedIndex => _selected;

    public void Initialize(string modDir, Func<string, bool>? hasAudio = null)
    {
        if (_initialized) return;
        _initialized = true;
        _modDir = modDir;
        _hasAudio = hasAudio ?? (_ => false);
        Layer = 200;

        LoadTextures();

        var viewport = GetViewport().GetVisibleRect().Size;
        var center = new Vector2(viewport.X / 2f, viewport.Y / 2f);

        BuildBackground(center);
        BuildSlots(center);
        BuildCenterCluster(center);

        Visible = false;
        GD.Print($"[VR][Wheel] Initialize done. center={center}, modDir={modDir}");
    }

    // -------------------------------------------------------------------------
    // Texture loading from disk
    // -------------------------------------------------------------------------

    private void LoadTextures()
    {
        if (_modDir == null) return;
        var texDir = Path.Combine(_modDir, "textures");
        _texCard = LoadTex(Path.Combine(texDir, "card.png"));
        _texCardHover = LoadTex(Path.Combine(texDir, "card_hover.png"));
        _texCenterHub = LoadTex(Path.Combine(texDir, "center_hub.png"));
        _texOuterRing = LoadTex(Path.Combine(texDir, "outer_ring.png"));
    }

    private static Texture2D? LoadTex(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"[VR][Wheel] missing texture: {path}");
            return null;
        }
        var img = new Image();
        var err = img.Load(path);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[VR][Wheel] failed to load {path}: {err}");
            return null;
        }
        return ImageTexture.CreateFromImage(img);
    }

    // -------------------------------------------------------------------------
    // Background — soft glow + outer gold ring
    // -------------------------------------------------------------------------

    private void BuildBackground(Vector2 center)
    {
        // Soft dark backdrop disc behind the whole wheel
        var glow = MakeCircle(center, OuterRingSize / 2f + 40, 80, new Color("0e0a06ee"));
        AddChild(glow);

        // Outer decorative gold ring (texture)
        if (_texOuterRing != null)
        {
            var ring = new TextureRect
            {
                Texture = _texOuterRing,
                Size = new Vector2(OuterRingSize, OuterRingSize),
                Position = center - new Vector2(OuterRingSize / 2f, OuterRingSize / 2f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            AddChild(ring);
        }
    }

    // -------------------------------------------------------------------------
    // 8 parchment-card slots arranged radially
    // -------------------------------------------------------------------------

    private void BuildSlots(Vector2 center)
    {
        for (int i = 0; i < SectorCount; i++)
        {
            var angle = -Mathf.Pi / 2f + i * (2f * Mathf.Pi / SectorCount);
            var slotCenter = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SlotRadius;
            // Rotate the card so the narrow top points OUTWARD (top of card faces outside the wheel).
            var rotation = angle + Mathf.Pi / 2f;

            var card = new TextureRect
            {
                Texture = _texCard,
                Size = new Vector2(CardWidth, CardHeight),
                Position = slotCenter,
                PivotOffset = new Vector2(CardWidth / 2f, CardHeight / 2f),
                Rotation = rotation,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            // Center the rotated card on slotCenter (account for pivot offset)
            card.Position = slotCenter - new Vector2(CardWidth / 2f, CardHeight / 2f);
            AddChild(card);
            _slotCards[i] = card;

            // Icon in the upper third of the card
            var icon = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(60, 60),
            };
            icon.AddThemeColorOverride("font_color", new Color("2a1d10"));
            icon.AddThemeFontSizeOverride("font_size", 32);
            // Position relative to slot center, accounting for rotation.
            var iconLocalY = -28f;
            icon.Position = slotCenter
                + new Vector2(Mathf.Cos(rotation - Mathf.Pi/2) * 0,
                              Mathf.Sin(rotation - Mathf.Pi/2) * 0)
                - new Vector2(30, 30) + RotateVec(new Vector2(0, iconLocalY), rotation);
            icon.PivotOffset = new Vector2(30, 30);
            icon.Rotation = rotation;
            AddChild(icon);
            _slotIcons[i] = icon;

            // Phrase text in the lower portion of the card
            var text = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(110, 28),
                ClipText = true,
            };
            text.AddThemeColorOverride("font_color", new Color("2a1d10"));
            text.AddThemeFontSizeOverride("font_size", 14);
            var textLocalY = 38f;
            text.Position = slotCenter - new Vector2(55, 14) + RotateVec(new Vector2(0, textLocalY), rotation);
            text.PivotOffset = new Vector2(55, 14);
            text.Rotation = rotation;
            AddChild(text);
            _slotTexts[i] = text;

            // Audio badge near top-right of card
            var badge = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(20, 20),
            };
            badge.AddThemeColorOverride("font_color", new Color("d4a937"));
            badge.AddThemeColorOverride("font_outline_color", new Color("2a1d10"));
            badge.AddThemeConstantOverride("outline_size", 3);
            badge.AddThemeFontSizeOverride("font_size", 13);
            var badgeLocalPos = new Vector2(36, -56);
            badge.Position = slotCenter - new Vector2(10, 10) + RotateVec(badgeLocalPos, rotation);
            badge.PivotOffset = new Vector2(10, 10);
            badge.Rotation = rotation;
            AddChild(badge);
            _slotAudioBadges[i] = badge;

            // Sector number outside the wheel (no rotation, world-aligned)
            var num = new Label
            {
                Text = (i + 1).ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(28, 22),
            };
            num.AddThemeColorOverride("font_color", new Color("d4a937"));
            num.AddThemeColorOverride("font_outline_color", new Color("0a0703"));
            num.AddThemeConstantOverride("outline_size", 4);
            num.AddThemeFontSizeOverride("font_size", 18);
            var numRadius = OuterRingSize / 2f + 14;
            var numCenter = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * numRadius;
            num.Position = numCenter - new Vector2(14, 11);
            AddChild(num);
            _slotNumbers[i] = num;
        }
    }

    private static Vector2 RotateVec(Vector2 v, float angle)
    {
        var c = Mathf.Cos(angle);
        var s = Mathf.Sin(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    // -------------------------------------------------------------------------
    // Center: hub texture + selection text + hint + direction line
    // -------------------------------------------------------------------------

    private void BuildCenterCluster(Vector2 center)
    {
        if (_texCenterHub != null)
        {
            var hub = new TextureRect
            {
                Texture = _texCenterHub,
                Size = new Vector2(CenterHubSize, CenterHubSize),
                Position = center - new Vector2(CenterHubSize / 2f, CenterHubSize / 2f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            AddChild(hub);
        }

        _directionLine = new Line2D
        {
            Width = 4f,
            DefaultColor = new Color("ffd76acc"),
            Antialiased = true,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
            Visible = false,
        };
        _directionLine.AddPoint(center);
        _directionLine.AddPoint(center);
        AddChild(_directionLine);

        _hintLabel = new Label
        {
            Text = "释放发送   ·   Esc 取消",
            HorizontalAlignment = HorizontalAlignment.Center,
            Size = new Vector2(300, 24),
            Position = center + new Vector2(-150, -OuterRingSize / 2f - 56),
        };
        _hintLabel.AddThemeColorOverride("font_color", new Color("c8a878"));
        _hintLabel.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_hintLabel);

        _centerLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Size = new Vector2(420, 36),
            Position = center + new Vector2(-210, -OuterRingSize / 2f - 34),
        };
        _centerLabel.AddThemeColorOverride("font_color", new Color("ffd76a"));
        _centerLabel.AddThemeColorOverride("font_outline_color", new Color("0a0703"));
        _centerLabel.AddThemeConstantOverride("outline_size", 6);
        _centerLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_centerLabel);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public void OpenWheel(IList<string> lineTexts)
    {
        if (!_initialized) return;
        _selected = -1;
        _texts = new List<string>(lineTexts);
        ResetAllSlotVisuals();
        if (_centerLabel != null) _centerLabel.Text = "";
        if (_directionLine != null) _directionLine.Visible = false;
        Visible = true;
    }

    public void CloseWheel()
    {
        ResetAllSlotVisuals();
        _selected = -1;
        Visible = false;
    }

    public void SetSelectedFromMouse(Vector2 mouseDelta)
    {
        if (mouseDelta.LengthSquared() < MouseDeadZoneSquared)
        {
            UpdateDirectionLine(null);
            Highlight(-1);
            return;
        }
        UpdateDirectionLine(mouseDelta);
        var angle = Mathf.Atan2(mouseDelta.Y, mouseDelta.X) + Mathf.Pi / 2f + Mathf.Pi / SectorCount;
        if (angle < 0) angle += 2f * Mathf.Pi;
        var idx = (int)(angle / (2f * Mathf.Pi / SectorCount)) % SectorCount;
        Highlight(idx);
    }

    private void ResetAllSlotVisuals()
    {
        for (int i = 0; i < SectorCount; i++)
        {
            var text = i < _texts.Count ? _texts[i] : "";
            var hasAudio = !string.IsNullOrEmpty(text) && (_hasAudio?.Invoke(text) ?? false);

            _slotTexts[i].Text = text;
            _slotIcons[i].Text = string.IsNullOrEmpty(text) ? "" : PickIcon(text);
            _slotAudioBadges[i].Text = string.IsNullOrEmpty(text) ? "" : (hasAudio ? IconAudio : IconNoAudio);
            _slotAudioBadges[i].AddThemeColorOverride("font_color", hasAudio ? new Color("d4a937") : new Color("6a5238"));
            _slotCards[i].Texture = _texCard;
        }
    }

    private void Highlight(int idx)
    {
        if (idx == _selected) return;

        if (_selected >= 0 && _selected < SectorCount)
            _slotCards[_selected].Texture = _texCard;

        _selected = idx;

        if (idx >= 0 && idx < SectorCount)
            _slotCards[idx].Texture = _texCardHover;

        if (_centerLabel != null)
            _centerLabel.Text = idx >= 0 && idx < _texts.Count ? _texts[idx] : "";
    }

    private void UpdateDirectionLine(Vector2? mouseDelta)
    {
        if (_directionLine == null) return;
        if (!mouseDelta.HasValue) { _directionLine.Visible = false; return; }
        var dir = mouseDelta.Value.Normalized();
        var viewport = GetViewport().GetVisibleRect().Size;
        var center = new Vector2(viewport.X / 2f, viewport.Y / 2f);
        _directionLine.SetPointPosition(0, center + dir * (CenterHubSize / 2f + 4));
        _directionLine.SetPointPosition(1, center + dir * (CenterHubSize / 2f + 60));
        _directionLine.Visible = true;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string PickIcon(string phrase)
    {
        foreach (var (kw, glyph) in IconRules)
            if (phrase.Contains(kw)) return glyph;
        return "✦";
    }

    private static Polygon2D MakeCircle(Vector2 c, float r, int segs, Color color)
    {
        var pts = new Vector2[segs];
        for (int i = 0; i < segs; i++)
        {
            var a = i * Mathf.Tau / segs;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return new Polygon2D { Polygon = pts, Color = color };
    }
}
