using Godot;
using System;
using System.Collections.Generic;
using VoiceRoulette.Config;
using VoiceRoulette.Lines;

namespace VoiceRoulette.UI;

// Settings UI styled to the user-supplied design mock (warm earthy palette,
// 思源宋体 font from the game's res:// resources, parchment-tag section
// headers, minimal row design).
//
// Layout:
//   ┌─────────────────────────────────────────────────────────────────────┐
//   │ StS2 Chat Wheel · 设置                                  按 N 关闭   │
//   │ ⚠ (API Key warning, only when missing)                              │
//   │ [ 轮盘配置 ] [ 快捷键 ]                                              │
//   ├─────────────────────────────────────────────────────────────────────┤
//   │ ① 当前轮盘            ② 语音库                                      │
//   │   1 🔊 好牌！        [全部][战斗][撤退]…   [搜索 ____________]      │
//   │   2 🔊 打精英怪！                                                   │
//   │   ...                  🔊 进攻!  🔊 防御!  🔊 我来挡                │
//   │   8 🔊 干得漂亮       🔊 集合!  🔊 打精英  …                        │
//   │                                                                     │
//   │ ③ 编辑 #N             装备中：#N <text>                             │
//   │   [text input]                                                      │
//   │   ●语音 [情感▾] [试听]                                               │
//   ├─────────────────────────────────────────────────────────────────────┤
//   │                       [重置默认] [取消] [保存]                       │
//   └─────────────────────────────────────────────────────────────────────┘
public sealed partial class SettingsScreen : CanvasLayer
{
    private const int LineCount = 8;
    private const int MaxTextLen = 20;

    // ── Palette aliases pulled from StsTheme.MenuXxx (warm earthy) ───────────
    private static readonly Color OverlayBg     = StsTheme.ScreenBackdrop;
    private static readonly Color PanelBg       = StsTheme.MenuBg;
    private static readonly Color SectionBg     = StsTheme.MenuBgAlt;
    private static readonly Color RowBgSel      = StsTheme.MenuBgHover;
    private static readonly Color BannerBg      = StsTheme.MenuBgSlate;
    private static readonly Color BorderC       = StsTheme.MenuBorder;
    private static readonly Color DividerC      = StsTheme.MenuDivider;
    private static readonly Color TextC         = StsTheme.MenuText;
    private static readonly Color TextDimC      = StsTheme.MenuTextDim;
    private static readonly Color AccentC       = StsTheme.MenuAccent;
    private static readonly Color InputBgC      = StsTheme.MenuInputBg;
    private static readonly Color RedAccentC    = StsTheme.MenuRedAccent;
    private static readonly Color RedAccentHov  = new("C84A40");
    private static readonly Color WarningC      = StsTheme.Orange;

    // ── Static option lists ──────────────────────────────────────────────────
    private static readonly (string label, string apiValue)[] EmotionOptions =
    {
        ("正常", "novel_dialog"),
        ("开心", "happy"),
        ("愤怒", "angry"),
        ("无奈", "sad"),
        ("委屈", "sorry"),
    };

    private static readonly (string label, string voiceId)[] VoiceOptions =
    {
        ("爽快思思（女）",   "zh_female_shuangkuaisisi_uranus_bigtts"),
        ("猪八戒（男）",     "zh_male_zhubajie_uranus_bigtts"),
        ("熊二（男）",       "zh_male_xionger_uranus_bigtts"),
        ("猴哥（男）",       "zh_male_sunwukong_uranus_bigtts"),
        ("海绵宝宝（男）",   "zh_male_liangsangmengzai_uranus_bigtts"),
    };

    private static readonly Key[] CapturableKeys =
    {
        Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J,
        Key.K, Key.L, Key.M, Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T,
        Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z,
        Key.Key0, Key.Key1, Key.Key2, Key.Key3, Key.Key4,
        Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6,
        Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
        Key.Semicolon, Key.Apostrophe, Key.Backslash,
        Key.Bracketleft, Key.Bracketright, Key.Comma, Key.Period, Key.Slash,
        Key.Quoteleft, Key.Minus, Key.Equal,
    };

    // ── Wired by Initialize ──────────────────────────────────────────────────
    private ConfigStore? _config;
    private Action? _onSaved;
    private Action<string, string?, string>? _previewCallback;
    private string _toggleKeyHint = ";";
    private bool _initialized;
    private SceneTree? _tree;

    // ── Persistent UI elements ───────────────────────────────────────────────
    private Label? _apiKeyWarning;
    private Label? _statusLabel;
    private readonly Button[] _tabButtons = new Button[2];
    private readonly Control[] _tabContents = new Control[2];
    private int _activeTab = 0;

    // Tab 1: slots + editor + library
    private readonly Button[] _slotRows = new Button[LineCount]; // ← legacy, repurposed as mini-wheel buttons
    private LineEdit? _editorText;
    private CheckButton? _editorVoiceToggle;
    private OptionButton? _editorEmotion;
    private Button? _editorPreviewBtn;
    private Label? _editorTitleLabel;
    private Label? _editorEmotionLabel;

    // Library: items rebuilt dynamically from _stagedLibrary on each refresh.
    // Each visible entry is shown as an HBoxContainer-like pair of buttons
    // (main = apply, × = delete). We just pool both kinds and recycle.
    private readonly List<Button> _libraryApplyButtons = new();
    private readonly List<Button> _libraryDeleteButtons = new();
    private Label? _libraryEquipLabel;
    private float _libGridCellW, _libGridCellH;
    private float _libGridColGap, _libGridRowGap;
    private int _libGridCols;
    private const float LibDeleteSize = 22f;  // floating × badge inside the apply button (top-right corner)
    private Control? _libGridInner;        // scroll container's inner Control (for adding/removing buttons)

    // Add-form (below the grid)
    private LineEdit? _addText;
    private OptionButton? _addEmotion;
    private Button? _addButton;

    // Tab 2: hotkeys + voice
    private OptionButton? _voicePicker;
    private Button? _wheelKeyButton;
    private Button? _settingsKeyButton;

    // ── Staged state ────────────────────────────────────────────────────────
    private readonly LineEntry[] _stagedLines = new LineEntry[LineCount];
    private readonly List<LibraryEntry> _stagedLibrary = new();
    private int _selectedSlot = 0;
    private int _capturingSlot = -1;
    private Key _stagedWheelKey = Key.Y;
    private Key _stagedSettingsKey = Key.Semicolon;
    private string _stagedVoiceId = "";

    public bool IsOpen => Visible;

    public void Initialize(
        ConfigStore config,
        Action onSaved,
        Action<string, string?, string> previewCallback,
        string toggleKeyHint = ";")
    {
        if (_initialized) return;
        _initialized = true;
        _config = config;
        _onSaved = onSaved;
        _previewCallback = previewCallback;
        _toggleKeyHint = toggleKeyHint;
        Layer = 250;

        for (var i = 0; i < LineCount; i++)
            _stagedLines[i] = new LineEntry { Id = $"slot_{i}", Text = "" };

        BuildUi();
        Visible = false;

        _tree = (SceneTree)Engine.GetMainLoop();
        _tree.ProcessFrame += OnTick;

        GD.Print("[VR][Settings] Initialize done");
    }

    public void Toggle()
    {
        if (!_initialized || _config == null) return;
        Visible = !Visible;
        if (Visible)
        {
            LoadFromConfig();
            UpdateApiKeyWarning();
            UpdateStatus("");
            SwitchTab(0);
        }
    }

    // -------------------------------------------------------------------------
    // Layout dimensions (smaller panel per design mock; everything tightened)
    // -------------------------------------------------------------------------
    private const float PanelW       = 1000f;
    private const float PanelH       = 640f;
    private const float Pad          = 24f;
    private const float HeaderH      = 76f;
    private const float TabStripY    = 76f;
    private const float ContentY     = 116f;
    private const float FooterH      = 60f;
    private const float ContentH     = PanelH - ContentY - FooterH;  // 448

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var panelSize = new Vector2(PanelW, PanelH);
        var panelPos = (viewport - panelSize) / 2f;

        var bg = new ColorRect { Color = OverlayBg, Size = viewport };
        AddChild(bg);

        var panel = new Panel { Size = panelSize, Position = panelPos };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = PanelBg, BorderColor = BorderC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            ShadowColor = OverlayBg, ShadowSize = 24,
        });
        AddChild(panel);

        BuildHeader(panel);
        BuildTabStrip(panel);
        BuildTabSlots(panel);
        BuildTabHotkeys(panel);
        BuildFooter(panel);
    }

    private void BuildHeader(Panel panel)
    {
        var title = MakeLabel("StS2 Chat Wheel · 设置", AccentC, StsTheme.FontH1, weight: StsFonts.FontWeight.Bold);
        title.Position = new Vector2(Pad, 18);
        title.Size = new Vector2(500, 32);
        panel.AddChild(title);

        var hint = MakeLabel($"按 {_toggleKeyHint} 关闭", TextDimC, StsTheme.FontCaption);
        hint.Position = new Vector2(PanelW - 140 - Pad, 26);
        hint.Size = new Vector2(140, 22);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        panel.AddChild(hint);

        _apiKeyWarning = MakeLabel("", WarningC, StsTheme.FontCaption);
        _apiKeyWarning.Position = new Vector2(Pad, 50);
        _apiKeyWarning.Size = new Vector2(PanelW - 2 * Pad, 18);
        _apiKeyWarning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        panel.AddChild(_apiKeyWarning);
    }

    private void BuildTabStrip(Panel panel)
    {
        var labels = new[] { "轮盘配置", "设置" };
        const float tabW = 110f;
        const float gap = 6f;
        for (var i = 0; i < labels.Length; i++)
        {
            var btn = MakeTabButton(labels[i]);
            btn.Position = new Vector2(Pad + i * (tabW + gap), TabStripY);
            btn.CustomMinimumSize = new Vector2(tabW, 32);
            var captured = i;
            btn.Pressed += () => SwitchTab(captured);
            panel.AddChild(btn);
            _tabButtons[i] = btn;
        }

        var divider = new ColorRect
        {
            Color = DividerC,
            Position = new Vector2(Pad, TabStripY + 36),
            Size = new Vector2(PanelW - 2 * Pad, 1),
        };
        panel.AddChild(divider);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tab 1 — slots + editor (left) + library (right)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildTabSlots(Panel panel)
    {
        var container = new Control
        {
            Position = new Vector2(Pad, ContentY),
            Size = new Vector2(PanelW - 2 * Pad, ContentH),
        };
        panel.AddChild(container);
        _tabContents[0] = container;

        const float leftW = 380f;
        const float gapX = 24f;

        // ── Section banner: 当前轮盘 ────────────────────────────────────────
        BuildSectionBanner(container, "当前轮盘", new Vector2(0, 0), leftW);

        // ── Mini wheel preview (interactive: click a position to select) ────
        const float wheelTopY = 36f;
        const float wheelSize = 280f;
        BuildWheelPreview(container, originX: (leftW - wheelSize) / 2, originY: wheelTopY, size: wheelSize);

        // ── Section banner: 编辑 #N (banner doubles as title — no redundancy)
        const float editorY = wheelTopY + wheelSize + 16;
        _editorTitleLabel = BuildSectionBannerDynamic(container, "编辑 #1", new Vector2(0, editorY), leftW);

        // Editor row
        const float ey0 = editorY + 38;
        _editorText = new LineEdit
        {
            Position = new Vector2(0, ey0),
            Size = new Vector2(leftW, 32),
            MaxLength = MaxTextLen,
            PlaceholderText = $"自定义文本（最多 {MaxTextLen} 字）",
        };
        StyleLineEdit(_editorText);
        _editorText.TextChanged += t => OnEditorTextChanged(t);
        container.AddChild(_editorText);

        _editorVoiceToggle = new CheckButton
        {
            Position = new Vector2(0, ey0 + 42),
            CustomMinimumSize = new Vector2(72, 32),
            Text = "语音",
        };
        StsFonts.ApplyTo(_editorVoiceToggle);
        _editorVoiceToggle.AddThemeColorOverride("font_color", TextC);
        _editorVoiceToggle.AddThemeFontSizeOverride("font_size", StsTheme.FontCaption);
        _editorVoiceToggle.Toggled += on => OnEditorVoiceToggled(on);
        container.AddChild(_editorVoiceToggle);

        _editorEmotionLabel = MakeLabel("情感", TextDimC, StsTheme.FontCaption);
        _editorEmotionLabel.Position = new Vector2(94, ey0 + 48);
        _editorEmotionLabel.Size = new Vector2(40, 24);
        container.AddChild(_editorEmotionLabel);

        _editorEmotion = new OptionButton
        {
            Position = new Vector2(138, ey0 + 44),
            Size = new Vector2(120, 32),
        };
        for (var k = 0; k < EmotionOptions.Length; k++)
            _editorEmotion.AddItem(EmotionOptions[k].label, k);
        _editorEmotion.ItemSelected += idx => OnEditorEmotionChanged((int)idx);
        StyleOptionButton(_editorEmotion);
        container.AddChild(_editorEmotion);

        _editorPreviewBtn = MakeButton("▶ 试听", SectionBg, RowBgSel);
        _editorPreviewBtn.Position = new Vector2(leftW - 96, ey0 + 44);
        _editorPreviewBtn.CustomMinimumSize = new Vector2(96, 32);
        _editorPreviewBtn.Pressed += OnPreview;
        container.AddChild(_editorPreviewBtn);

        // ── Right column: library ───────────────────────────────────────────
        const float rightX = leftW + gapX;
        var rightW = (PanelW - 2 * Pad) - rightX;
        BuildLibraryInPlace(container, originX: rightX, originY: 0, width: rightW, height: ContentH);
    }

    // Mini wheel preview matching the in-game voice-wheel style:
    //   • 8 transparent text-only buttons at radial positions (no boxes)
    //   • per-sector alignment so all items look equidistant from the hub
    //   • round center hub (matches in-game wheel) with a gold dot
    //   • a gold arrow OUTSIDE the hub that rotates to point at the selected slot
    private Polygon2D? _previewSelectionArrow;

    private void BuildWheelPreview(Control parent, float originX, float originY, float size)
    {
        var wheelCenter = new Vector2(originX + size / 2, originY + size / 2);
        var radius = size / 2 - 50;     // text anchor radius (closer to hub)
        const float btnW = 110f;
        const float btnH = 24f;

        for (var i = 0; i < LineCount; i++)
        {
            var angle = -Mathf.Pi / 2f + i * (Mathf.Tau / LineCount);
            var anchor = wheelCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Per-sector alignment (same scheme as the in-game wheel):
            //   top (0) / bottom (4)             → centered
            //   right side  (1, 2, 3)            → left-aligned, anchor at LEFT edge
            //   left side   (5, 6, 7)            → right-aligned, anchor at RIGHT edge
            HorizontalAlignment ha;
            Vector2 btnPos;
            if (i == 0 || i == 4)
            {
                ha = HorizontalAlignment.Center;
                btnPos = anchor - new Vector2(btnW / 2, btnH / 2);
            }
            else if (i >= 1 && i <= 3)
            {
                ha = HorizontalAlignment.Left;
                btnPos = anchor - new Vector2(0, btnH / 2);
            }
            else
            {
                ha = HorizontalAlignment.Right;
                btnPos = anchor - new Vector2(btnW, btnH / 2);
            }

            var btn = MakeWheelPreviewButton(ha);
            btn.Position = btnPos;
            btn.CustomMinimumSize = new Vector2(btnW, btnH);
            btn.Size = new Vector2(btnW, btnH);
            var captured = i;
            btn.Pressed += () => SelectSlot(captured);
            parent.AddChild(btn);
            _slotRows[i] = btn;
        }

        // ── Round center hub: dim disc + gold ring + tiny gold dot ──────────
        const float hubR = 18f;
        const int segs = 48;
        var hubPts = new Vector2[segs];
        for (var k = 0; k < segs; k++)
        {
            var a = k * Mathf.Tau / segs;
            hubPts[k] = wheelCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * hubR;
        }
        parent.AddChild(new Polygon2D { Polygon = hubPts, Color = SectionBg });

        var hubBorder = new Line2D
        {
            DefaultColor = AccentC,
            Width = 1.5f,
            Antialiased = true,
            Closed = true,
        };
        foreach (var p in hubPts) hubBorder.AddPoint(p);
        parent.AddChild(hubBorder);

        // Tiny center dot
        var dotPts = new Vector2[16];
        for (var k = 0; k < 16; k++)
        {
            var a = k * Mathf.Tau / 16;
            dotPts[k] = wheelCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2f;
        }
        parent.AddChild(new Polygon2D { Polygon = dotPts, Color = AccentC });

        // ── Selection arrow OUTSIDE the hub (rotates on select) ─────────────
        const float tipR  = 30f;
        const float baseR = 22f;
        _previewSelectionArrow = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new( 0, -tipR),    // tip outward
                new(-5, -baseR),   // left base
                new( 5, -baseR),   // right base
            },
            Color = AccentC,
            Position = wheelCenter,
            Visible = false,
        };
        parent.AddChild(_previewSelectionArrow);
    }

    private static Button MakeWheelPreviewButton(HorizontalAlignment ha)
    {
        var b = new Button
        {
            Text = "",
            Alignment = ha,
            ClipText = true,
        };
        StsFonts.ApplyTo(b, StsFonts.FontWeight.Bold);
        ApplyWheelPreviewStyle(b, selected: false);
        return b;
    }

    // Transparent style: no border, no fill, just colored text. Selected gets
    // gold text; hover gets gold too. Mirrors the in-game wheel's minimalism.
    private static void ApplyWheelPreviewStyle(Button b, bool selected)
    {
        var transparent = new StyleBoxEmpty();
        b.AddThemeStyleboxOverride("normal", transparent);
        b.AddThemeStyleboxOverride("hover", transparent);
        b.AddThemeStyleboxOverride("pressed", transparent);
        b.AddThemeStyleboxOverride("focus", transparent);
        b.AddThemeColorOverride("font_color", selected ? AccentC : TextC);
        b.AddThemeColorOverride("font_hover_color", AccentC);
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontCaption);
    }

    // Parchment-tag style banner: dark slate fill, gold left bar, cream text.
    private void BuildSectionBanner(Control parent, string text, Vector2 pos, float width)
    {
        BuildSectionBannerDynamic(parent, text, pos, width);
    }

    // Same as BuildSectionBanner but returns the Label so the caller can update
    // its text later (e.g. "② 编辑 #N" where N changes when the user picks a slot).
    private Label BuildSectionBannerDynamic(Control parent, string text, Vector2 pos, float width)
    {
        const float h = 30f;
        var bg = new Panel
        {
            Position = pos,
            Size = new Vector2(width, h),
        };
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = BannerBg,
            BorderColor = AccentC,
            BorderWidthLeft = 3, BorderWidthRight = 0, BorderWidthTop = 0, BorderWidthBottom = 0,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        parent.AddChild(bg);

        var lbl = MakeLabel(text, AccentC, StsTheme.FontBody, weight: StsFonts.FontWeight.Bold);
        lbl.Position = new Vector2(pos.X + 14, pos.Y + 4);
        lbl.Size = new Vector2(width - 28, h - 8);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        parent.AddChild(lbl);
        return lbl;
    }

    private void BuildLibraryInPlace(Control container, float originX, float originY, float width, float height)
    {
        // ── Section banner ──
        BuildSectionBanner(container, "语音库（点词条→写入选中槽位 · × 删除）", new Vector2(originX, originY), width);

        // ── Equip footer (bottom) ──
        var equipY = originY + height - 36;
        var equipBg = new Panel
        {
            Position = new Vector2(originX, equipY),
            Size = new Vector2(width, 30),
        };
        equipBg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = SectionBg, BorderColor = BorderC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 3, ContentMarginBottom = 3,
        });
        container.AddChild(equipBg);

        _libraryEquipLabel = MakeLabel("", TextC, StsTheme.FontCaption);
        _libraryEquipLabel.Position = new Vector2(12, 6);
        _libraryEquipLabel.Size = new Vector2(width - 24, 20);
        equipBg.AddChild(_libraryEquipLabel);

        // ── Add-form row (just above equip footer) ──
        var addY = equipY - 36;
        BuildAddForm(container, originX, addY, width);

        // ── Phrase grid (between banner and add-form) ──
        const int cols = 3;
        const float colGap = 6f;
        const float rGap = 4f;
        const float cellH = 28f;
        var cellW = (width - colGap * (cols - 1)) / cols;
        var gridY = originY + 36;            // immediately under the banner
        var gridHeight = addY - gridY - 6;

        _libGridCellW = cellW;
        _libGridCellH = cellH;
        _libGridColGap = colGap;
        _libGridRowGap = rGap;
        _libGridCols = cols;

        var scroll = new ScrollContainer
        {
            Position = new Vector2(originX, gridY),
            Size = new Vector2(width, gridHeight),
        };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        container.AddChild(scroll);

        _libGridInner = new Control
        {
            CustomMinimumSize = new Vector2(width - 12, 0),
        };
        scroll.AddChild(_libGridInner);

        // Buttons themselves are created lazily in RebuildLibraryGrid().
    }

    private void BuildAddForm(Control container, float originX, float y, float width)
    {
        // Layout: [text input ........] [emotion ▾] [添加]
        const float emoW = 100f;
        const float btnW = 64f;        // bumped up — single "+" was too small to click reliably
        const float h    = 32f;
        const float gap  = 6f;
        var textW = width - emoW - btnW - gap * 2;

        _addText = new LineEdit
        {
            Position = new Vector2(originX, y),
            Size = new Vector2(textW, h),
            CustomMinimumSize = new Vector2(textW, h),
            PlaceholderText = "新词条文本…",
            MaxLength = MaxTextLen,
        };
        StyleLineEdit(_addText);
        container.AddChild(_addText);

        _addEmotion = new OptionButton
        {
            Position = new Vector2(originX + textW + gap, y),
            Size = new Vector2(emoW, h),
            CustomMinimumSize = new Vector2(emoW, h),
        };
        _addEmotion.AddItem("(无语音)", 0);
        for (var k = 0; k < EmotionOptions.Length; k++)
            _addEmotion.AddItem(EmotionOptions[k].label, k + 1);
        StyleOptionButton(_addEmotion);
        container.AddChild(_addEmotion);

        _addButton = MakeButton("添加", SectionBg, RowBgSel);
        _addButton.Position = new Vector2(originX + textW + emoW + gap * 2, y);
        _addButton.Size = new Vector2(btnW, h);
        _addButton.CustomMinimumSize = new Vector2(btnW, h);
        _addButton.AddThemeColorOverride("font_color", AccentC);
        _addButton.AddThemeFontSizeOverride("font_size", StsTheme.FontCaption);
        _addButton.Pressed += () =>
        {
            GD.Print("[VR][Settings] Add button pressed");
            OnAddLibraryEntry();
        };
        container.AddChild(_addButton);
    }

    // Recreate library grid buttons from scratch — called whenever _stagedLibrary
    // changes (add/delete/reset). Buttons are pooled per-entry as (apply, delete).
    private void RebuildLibraryGrid()
    {
        if (_libGridInner == null) return;

        foreach (var b in _libraryApplyButtons) b.QueueFree();
        foreach (var b in _libraryDeleteButtons) b.QueueFree();
        _libraryApplyButtons.Clear();
        _libraryDeleteButtons.Clear();

        for (var i = 0; i < _stagedLibrary.Count; i++)
        {
            // Apply button takes the full cell — it's the primary surface.
            var apply = MakeButton("", SectionBg, RowBgSel);
            apply.CustomMinimumSize = new Vector2(_libGridCellW, _libGridCellH);
            apply.ClipText = true;
            apply.Alignment = HorizontalAlignment.Left;
            var idxA = i;
            apply.Pressed += () => OnLibraryClicked(idxA);
            _libGridInner.AddChild(apply);
            _libraryApplyButtons.Add(apply);

            // Delete × is a transparent badge overlaid on the apply button's
            // top-right corner. No border in normal state → no double-line
            // collision with the apply button's border. Only visible on hover.
            var del = MakeDeleteBadge();
            var idxD = i;
            del.Pressed += () => OnDeleteLibraryEntry(idxD);
            _libGridInner.AddChild(del);
            _libraryDeleteButtons.Add(del);
        }

        var rows = (_stagedLibrary.Count + _libGridCols - 1) / _libGridCols;
        _libGridInner.CustomMinimumSize = new Vector2(_libGridInner.CustomMinimumSize.X,
                                                      rows * (_libGridCellH + _libGridRowGap));
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        var cols = _libGridCols;
        for (var i = 0; i < _stagedLibrary.Count && i < _libraryApplyButtons.Count; i++)
        {
            var entry = _stagedLibrary[i];
            var apply = _libraryApplyButtons[i];
            var del = _libraryDeleteButtons[i];
            apply.Visible = true;
            del.Visible = true;
            var icon = entry.Emotion != null ? "◀)) " : "      ";
            // Reserve right-side padding inside the apply text so the × badge
            // doesn't overlap the trailing characters.
            apply.Text = $"{icon}{entry.Text}";
            var row = i / cols;
            var col = i % cols;
            var cellX = col * (_libGridCellW + _libGridColGap);
            var cellY = row * (_libGridCellH + _libGridRowGap);
            apply.Position = new Vector2(cellX, cellY);
            // × overlays the apply button's right edge; vertically centered.
            del.CustomMinimumSize = new Vector2(LibDeleteSize, LibDeleteSize);
            del.Position = new Vector2(
                cellX + _libGridCellW - LibDeleteSize - 4,
                cellY + (_libGridCellH - LibDeleteSize) / 2);
        }
        UpdateLibraryEquipLabel();
    }

    private void UpdateLibraryEquipLabel()
    {
        if (_libraryEquipLabel == null) return;
        var entry = _stagedLines[_selectedSlot];
        var current = string.IsNullOrEmpty(entry.Text) ? "(空)" : entry.Text;
        _libraryEquipLabel.Text = $"装备中：#{_selectedSlot + 1}  {current}";
    }

    private void OnLibraryClicked(int libIdx)
    {
        if (libIdx < 0 || libIdx >= _stagedLibrary.Count) return;
        var libEntry = _stagedLibrary[libIdx];
        var slotEntry = _stagedLines[_selectedSlot];

        var fromLibText = libEntry.Text.Length > MaxTextLen ? libEntry.Text[..MaxTextLen] : libEntry.Text;
        var fromLibEmotion = libEntry.Emotion;

        if (string.IsNullOrEmpty(slotEntry.Text))
        {
            // Wheel slot is empty → just copy library entry into the slot,
            // leave the library unchanged. (Empty text in library = clutter.)
            slotEntry.Text = fromLibText;
            slotEntry.Emotion = fromLibEmotion;
            UpdateStatus($"#{_selectedSlot + 1} ← {fromLibText}（点保存生效）");
        }
        else
        {
            // SWAP: wheel slot text moves into library, library text moves
            // into the wheel slot. Category stays with the library entry
            // (it's a pure-metadata field; no semantic match to swap).
            var oldSlotText = slotEntry.Text;
            var oldSlotEmotion = slotEntry.Emotion;

            slotEntry.Text = fromLibText;
            slotEntry.Emotion = fromLibEmotion;
            libEntry.Text = oldSlotText;
            libEntry.Emotion = oldSlotEmotion;

            UpdateStatus($"#{_selectedSlot + 1} ↔ {fromLibText}（与「{oldSlotText}」交换，点保存生效）");
        }

        RefreshSlotRow(_selectedSlot);
        RefreshEditor();
        UpdateLibraryEquipLabel();
        RefreshLibrary();   // re-render library entry text (changed on swap)
    }

    private void OnAddLibraryEntry()
    {
        if (_addText == null || _addEmotion == null) return;
        var text = _addText.Text.Trim();
        if (string.IsNullOrEmpty(text)) { UpdateStatus("文本不能为空"); return; }

        // Emotion dropdown: index 0 = "(无语音)", 1+ = EmotionOptions
        string? emotion = null;
        var emoIdx = _addEmotion.Selected;
        if (emoIdx > 0)
            emotion = EmotionOptions[Math.Clamp(emoIdx - 1, 0, EmotionOptions.Length - 1)].apiValue;

        _stagedLibrary.Add(new LibraryEntry { Text = text, Category = "通用", Emotion = emotion });
        _addText.Text = "";
        RebuildLibraryGrid();
        UpdateStatus($"已添加：{text}（点保存生效）");
    }

    private void OnDeleteLibraryEntry(int idx)
    {
        if (idx < 0 || idx >= _stagedLibrary.Count) return;
        var removed = _stagedLibrary[idx].Text;
        _stagedLibrary.RemoveAt(idx);
        RebuildLibraryGrid();
        UpdateStatus($"已删除：{removed}（点保存生效）");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tab 2 — hotkeys + voice picker
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildTabHotkeys(Panel panel)
    {
        var container = new Control
        {
            Position = new Vector2(Pad, ContentY),
            Size = new Vector2(PanelW - 2 * Pad, ContentH),
        };
        panel.AddChild(container);
        _tabContents[1] = container;

        // Section: hotkeys
        BuildSectionBanner(container, "快捷键（点按钮再按键来更改 · Esc 取消）", new Vector2(0, 0), 600);

        var wheelLbl = MakeLabel("打开轮盘", TextC, StsTheme.FontBody);
        wheelLbl.Position = new Vector2(0, 50);
        wheelLbl.Size = new Vector2(110, 32);
        wheelLbl.VerticalAlignment = VerticalAlignment.Center;
        container.AddChild(wheelLbl);

        _wheelKeyButton = MakeButton("Y", SectionBg, RowBgSel);
        _wheelKeyButton.Position = new Vector2(120, 48);
        _wheelKeyButton.CustomMinimumSize = new Vector2(120, 36);
        _wheelKeyButton.Pressed += () => StartCapture(0);
        container.AddChild(_wheelKeyButton);

        var settingsLbl = MakeLabel("打开设置", TextC, StsTheme.FontBody);
        settingsLbl.Position = new Vector2(0, 96);
        settingsLbl.Size = new Vector2(110, 32);
        settingsLbl.VerticalAlignment = VerticalAlignment.Center;
        container.AddChild(settingsLbl);

        _settingsKeyButton = MakeButton(";", SectionBg, RowBgSel);
        _settingsKeyButton.Position = new Vector2(120, 94);
        _settingsKeyButton.CustomMinimumSize = new Vector2(120, 36);
        _settingsKeyButton.Pressed += () => StartCapture(1);
        container.AddChild(_settingsKeyButton);

        // Section: voice
        BuildSectionBanner(container, "音色", new Vector2(0, 160), 600);

        _voicePicker = new OptionButton
        {
            Position = new Vector2(0, 200),
            Size = new Vector2(360, 36),
        };
        for (var k = 0; k < VoiceOptions.Length; k++)
            _voicePicker.AddItem(VoiceOptions[k].label, k);
        _voicePicker.ItemSelected += idx => _stagedVoiceId = VoiceOptions[Math.Clamp(idx, 0, VoiceOptions.Length - 1)].voiceId;
        StyleOptionButton(_voicePicker);
        container.AddChild(_voicePicker);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Footer
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildFooter(Panel panel)
    {
        const float footerY = PanelH - 50f;

        _statusLabel = MakeLabel("", TextDimC, StsTheme.FontCaption);
        _statusLabel.Position = new Vector2(Pad, footerY + 8);
        _statusLabel.Size = new Vector2(PanelW - 360, 28);
        _statusLabel.VerticalAlignment = VerticalAlignment.Center;
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        panel.AddChild(_statusLabel);

        var resetBtn = MakeButton("重置默认", SectionBg, RowBgSel);
        resetBtn.Position = new Vector2(PanelW - 304, footerY);
        resetBtn.CustomMinimumSize = new Vector2(96, 36);
        resetBtn.Pressed += OnReset;
        panel.AddChild(resetBtn);

        var cancelBtn = MakeButton("取消", SectionBg, RowBgSel);
        cancelBtn.Position = new Vector2(PanelW - 200, footerY);
        cancelBtn.CustomMinimumSize = new Vector2(80, 36);
        cancelBtn.Pressed += () => Visible = false;
        panel.AddChild(cancelBtn);

        var saveBtn = MakeButton("保存", RedAccentC, RedAccentHov);
        saveBtn.Position = new Vector2(PanelW - 112, footerY);
        saveBtn.CustomMinimumSize = new Vector2(88, 36);
        saveBtn.Pressed += OnSave;
        panel.AddChild(saveBtn);
    }

    // -------------------------------------------------------------------------
    // Tab switching
    // -------------------------------------------------------------------------

    private void SwitchTab(int idx)
    {
        _activeTab = Math.Clamp(idx, 0, _tabContents.Length - 1);
        for (var i = 0; i < _tabContents.Length; i++)
        {
            if (_tabContents[i] != null) _tabContents[i].Visible = i == _activeTab;
            if (_tabButtons[i] != null) ApplyTabStyle(_tabButtons[i], active: i == _activeTab);
        }
        if (_activeTab == 0) RefreshLibrary();
    }

    // -------------------------------------------------------------------------
    // Slot row + editor binding (Tab 1)
    // -------------------------------------------------------------------------

    private void RefreshSlotRow(int i)
    {
        // Mini-wheel button: compact label (≤6 chars). Inline ◀)) marker
        // when voice is on, mirroring the in-game wheel's text format.
        var entry = _stagedLines[i];
        var hasVoice = entry.Emotion != null;
        var text = string.IsNullOrEmpty(entry.Text) ? "(空)" : entry.Text;
        var preview = text.Length > 6 ? text[..6] + "…" : text;
        var icon = hasVoice && !string.IsNullOrEmpty(entry.Text) ? "◀)) " : "";
        _slotRows[i].Text = icon + preview;
        ApplyWheelPreviewStyle(_slotRows[i], selected: i == _selectedSlot);
    }

    private void RefreshAllSlotRows()
    {
        for (var i = 0; i < LineCount; i++)
            RefreshSlotRow(i);
    }

    private void SelectSlot(int i)
    {
        _selectedSlot = i;
        RefreshAllSlotRows();
        RefreshEditor();
        UpdateLibraryEquipLabel();

        // Rotate the preview arrow to point at the newly-selected sector.
        if (_previewSelectionArrow != null)
        {
            var step = Mathf.Tau / LineCount;
            var angle = -Mathf.Pi / 2f + i * step;
            _previewSelectionArrow.Rotation = angle + Mathf.Pi / 2f;
            _previewSelectionArrow.Visible = true;
        }
    }

    private void RefreshEditor()
    {
        if (_editorText == null || _editorVoiceToggle == null || _editorEmotion == null) return;
        var entry = _stagedLines[_selectedSlot];
        if (_editorTitleLabel != null) _editorTitleLabel.Text = $"编辑 #{_selectedSlot + 1}";
        _editorText.Text = entry.Text;
        var hasVoice = entry.Emotion != null;
        _editorVoiceToggle.SetPressedNoSignal(hasVoice);
        _editorEmotion.Selected = EmotionIndex(entry.Emotion ?? "novel_dialog");
        ApplyEditorVoiceVisibility(hasVoice);
    }

    private void ApplyEditorVoiceVisibility(bool on)
    {
        if (_editorEmotionLabel != null) _editorEmotionLabel.Visible = on;
        if (_editorEmotion != null)      _editorEmotion.Visible = on;
        if (_editorPreviewBtn != null)   _editorPreviewBtn.Visible = on;
    }

    private void OnEditorTextChanged(string text)
    {
        _stagedLines[_selectedSlot].Text = text;
        RefreshSlotRow(_selectedSlot);
        UpdateLibraryEquipLabel();
    }

    private void OnEditorVoiceToggled(bool on)
    {
        if (on)
        {
            var idx = _editorEmotion?.Selected ?? 0;
            _stagedLines[_selectedSlot].Emotion = EmotionOptions[Math.Clamp(idx, 0, EmotionOptions.Length - 1)].apiValue;
        }
        else
        {
            _stagedLines[_selectedSlot].Emotion = null;
        }
        ApplyEditorVoiceVisibility(on);
        RefreshSlotRow(_selectedSlot);
    }

    private void OnEditorEmotionChanged(int idx)
    {
        if (_stagedLines[_selectedSlot].Emotion != null)
            _stagedLines[_selectedSlot].Emotion = EmotionOptions[Math.Clamp(idx, 0, EmotionOptions.Length - 1)].apiValue;
    }

    private void OnPreview()
    {
        if (_previewCallback == null) return;
        var entry = _stagedLines[_selectedSlot];
        if (string.IsNullOrEmpty(entry.Text)) { UpdateStatus($"#{_selectedSlot + 1} 没有文字，无法试听"); return; }
        if (entry.Emotion == null) { UpdateStatus($"#{_selectedSlot + 1} 语音已关闭"); return; }
        var voiceLabel = "?";
        for (var i = 0; i < VoiceOptions.Length; i++)
            if (VoiceOptions[i].voiceId == _stagedVoiceId) { voiceLabel = VoiceOptions[i].label; break; }
        var emotionLabel = EmotionLabel(entry.Emotion);
        UpdateStatus($"试听 #{_selectedSlot + 1}: {entry.Text}  [{emotionLabel} · {voiceLabel}]");
        _previewCallback(entry.Text, entry.Emotion, _stagedVoiceId);
    }

    // -------------------------------------------------------------------------
    // Sync between UI and config
    // -------------------------------------------------------------------------

    private void LoadFromConfig()
    {
        if (_config == null) return;

        _stagedWheelKey    = ParseKeyOrDefault(_config.Schema.Hotkey, Key.Y);
        _stagedSettingsKey = ParseKeyOrDefault(_config.Schema.SettingsHotkey, Key.Semicolon);
        UpdateHotkeyButtonLabels();

        _stagedVoiceId = _config.Schema.DefaultVoice;
        if (_voicePicker != null)
        {
            var idx = 0;
            for (var i = 0; i < VoiceOptions.Length; i++)
                if (VoiceOptions[i].voiceId == _stagedVoiceId) { idx = i; break; }
            _voicePicker.Selected = idx;
        }

        var lines = _config.Schema.Lines;
        for (var i = 0; i < LineCount; i++)
        {
            var src = i < lines.Count ? lines[i] : new LineEntry { Id = $"slot_{i}", Text = "" };
            _stagedLines[i] = new LineEntry { Id = src.Id, Text = src.Text, Emotion = src.Emotion };
        }

        // Deep-copy library so add/delete during a session can be cancelled.
        _stagedLibrary.Clear();
        foreach (var src in _config.Schema.Library)
            _stagedLibrary.Add(new LibraryEntry { Text = src.Text, Category = src.Category, Emotion = src.Emotion });

        _selectedSlot = 0;
        RefreshAllSlotRows();
        RefreshEditor();
        RebuildLibraryGrid();
    }

    // -------------------------------------------------------------------------
    // Buttons
    // -------------------------------------------------------------------------

    private void OnSave()
    {
        if (_config == null) return;
        _config.Schema.Hotkey         = _stagedWheelKey.ToString();
        _config.Schema.SettingsHotkey = _stagedSettingsKey.ToString();
        _config.Schema.DefaultVoice   = _stagedVoiceId;

        var lines = _config.Schema.Lines;
        while (lines.Count < LineCount)
            lines.Add(new LineEntry { Id = $"slot_{lines.Count}", Text = "" });
        for (var i = 0; i < LineCount; i++)
        {
            lines[i].Id = _stagedLines[i].Id;
            lines[i].Text = (_stagedLines[i].Text ?? "").Trim();
            lines[i].Emotion = _stagedLines[i].Emotion;
        }

        // Replace library wholesale with the staged copy (drops removed, keeps adds).
        _config.Schema.Library.Clear();
        foreach (var entry in _stagedLibrary)
            _config.Schema.Library.Add(new LibraryEntry { Text = entry.Text, Category = entry.Category, Emotion = entry.Emotion });

        _config.Save();
        _onSaved?.Invoke();
        Visible = false;
    }

    private void OnReset()
    {
        var defaults = LineEntry.Defaults();
        for (var i = 0; i < LineCount; i++)
        {
            _stagedLines[i] = new LineEntry
            {
                Id = i < defaults.Count ? defaults[i].Id : $"slot_{i}",
                Text = i < defaults.Count ? defaults[i].Text : "",
                Emotion = i < defaults.Count ? defaults[i].Emotion : null,
            };
        }
        RefreshAllSlotRows();
        RefreshEditor();
        UpdateStatus("已重置为默认（未保存，点保存生效）");
    }

    private void UpdateApiKeyWarning()
    {
        if (_config == null || _apiKeyWarning == null) return;
        var hasKey = !string.IsNullOrWhiteSpace(_config.Schema.Doubao.ApiKey);
        _apiKeyWarning.Text = hasKey
            ? ""
            : "⚠ 未配置 Doubao API Key — 语音功能不会工作。请在 data/config.jsonc 里填 doubao.apiKey。";
    }

    private void UpdateStatus(string msg)
    {
        if (_statusLabel != null) _statusLabel.Text = msg;
    }

    // -------------------------------------------------------------------------
    // Hotkey capture
    // -------------------------------------------------------------------------

    private void StartCapture(int slot)
    {
        _capturingSlot = slot;
        var btn = slot == 0 ? _wheelKeyButton : _settingsKeyButton;
        if (btn != null) btn.Text = "按一个键…";
        UpdateStatus("按下要绑定的键（Esc 取消）");
    }

    private void OnTick()
    {
        if (!Visible || _capturingSlot < 0) return;
        if (Godot.Input.IsKeyPressed(Key.Escape))
        {
            _capturingSlot = -1;
            UpdateHotkeyButtonLabels();
            UpdateStatus("已取消");
            return;
        }
        foreach (var k in CapturableKeys)
        {
            if (!Godot.Input.IsKeyPressed(k)) continue;
            var other = _capturingSlot == 0 ? _stagedSettingsKey : _stagedWheelKey;
            if (k == other)
            {
                _capturingSlot = -1;
                UpdateHotkeyButtonLabels();
                UpdateStatus($"键 {KeyDisplay(k)} 已被另一个快捷键占用");
                return;
            }
            if (_capturingSlot == 0) _stagedWheelKey = k;
            else                     _stagedSettingsKey = k;
            _capturingSlot = -1;
            UpdateHotkeyButtonLabels();
            UpdateStatus($"已绑定：{KeyDisplay(k)}（点保存生效）");
            return;
        }
    }

    private void UpdateHotkeyButtonLabels()
    {
        if (_wheelKeyButton != null)    _wheelKeyButton.Text    = KeyDisplay(_stagedWheelKey);
        if (_settingsKeyButton != null) _settingsKeyButton.Text = KeyDisplay(_stagedSettingsKey);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static int EmotionIndex(string apiValue)
    {
        for (var i = 0; i < EmotionOptions.Length; i++)
            if (EmotionOptions[i].apiValue == apiValue) return i;
        return 0;
    }

    private static string EmotionLabel(string apiValue)
    {
        for (var i = 0; i < EmotionOptions.Length; i++)
            if (EmotionOptions[i].apiValue == apiValue) return EmotionOptions[i].label;
        return apiValue;
    }

    private static Key ParseKeyOrDefault(string s, Key fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return Enum.TryParse<Key>(s, ignoreCase: true, out var k) ? k : fallback;
    }

    private static string KeyDisplay(Key k) => k switch
    {
        Key.Semicolon => ";", Key.Apostrophe => "'", Key.Backslash => "\\",
        Key.Bracketleft => "[", Key.Bracketright => "]",
        Key.Comma => ",", Key.Period => ".", Key.Slash => "/",
        Key.Quoteleft => "`", Key.Minus => "-", Key.Equal => "=",
        Key.Key0 => "0", Key.Key1 => "1", Key.Key2 => "2",
        Key.Key3 => "3", Key.Key4 => "4", Key.Key5 => "5",
        Key.Key6 => "6", Key.Key7 => "7", Key.Key8 => "8", Key.Key9 => "9",
        _ => k.ToString(),
    };

    // -------------------------------------------------------------------------
    // Style + factory helpers (with game font applied)
    // -------------------------------------------------------------------------

    private static Label MakeLabel(string text, Color color, int fontSize, StsFonts.FontWeight weight = StsFonts.FontWeight.Body)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeFontSizeOverride("font_size", fontSize);
        StsFonts.ApplyTo(l, weight);
        return l;
    }

    private static Button MakeSlotRowButton()
    {
        var b = new Button { Text = "", Alignment = HorizontalAlignment.Left, ClipText = true };
        StsFonts.ApplyTo(b);
        ApplyRowStyle(b, selected: false);
        return b;
    }

    // Row style: minimal — bottom-only divider, gold left bar when selected.
    private static void ApplyRowStyle(Button b, bool selected)
    {
        var box = new StyleBoxFlat
        {
            BgColor = selected ? RowBgSel : new Color(0, 0, 0, 0),
            BorderColor = AccentC,
            BorderWidthLeft = selected ? 3 : 0,
            BorderWidthRight = 0, BorderWidthTop = 0,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        // Bottom divider color shouldn't be gold for non-selected — that's too loud.
        if (!selected)
            box.BorderColor = DividerC;

        b.AddThemeStyleboxOverride("normal", box);
        var hover = (StyleBoxFlat)box.Duplicate();
        hover.BgColor = RowBgSel;
        hover.BorderColor = AccentC;
        hover.BorderWidthLeft = 3;
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", box);
        b.AddThemeColorOverride("font_color", TextC);
        b.AddThemeColorOverride("font_hover_color", AccentC);
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
    }

    private static Button MakeTabButton(string label)
    {
        var b = new Button { Text = label };
        StsFonts.ApplyTo(b, StsFonts.FontWeight.Bold);
        ApplyTabStyle(b, active: false);
        return b;
    }

    private static void ApplyTabStyle(Button b, bool active)
    {
        // Minimalist tab: text only, with a gold underline when active. Echoes
        // the design mock's "no chrome" tabs (visible only as text + thin rule).
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = AccentC,
            BorderWidthLeft = 0, BorderWidthRight = 0, BorderWidthTop = 0,
            BorderWidthBottom = active ? 2 : 0,
            CornerRadiusBottomLeft = 0, CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 0, CornerRadiusTopRight = 0,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", box);
        b.AddThemeColorOverride("font_color", active ? AccentC : TextC);
        b.AddThemeColorOverride("font_hover_color", AccentC);
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
    }

    private static Button MakeCategoryButton(string label)
    {
        var b = new Button { Text = label };
        StsFonts.ApplyTo(b);
        ApplyCategoryStyle(b, active: false);
        return b;
    }

    private static void ApplyCategoryStyle(Button b, bool active)
    {
        var box = new StyleBoxFlat
        {
            BgColor = active ? RowBgSel : SectionBg,
            BorderColor = active ? AccentC : BorderC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", box);
        b.AddThemeColorOverride("font_color", active ? AccentC : TextC);
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontCaption);
    }

    private static void StyleLineEdit(LineEdit e)
    {
        StsFonts.ApplyTo(e);
        var box = new StyleBoxFlat
        {
            BgColor = InputBgC, BorderColor = BorderC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        e.AddThemeStyleboxOverride("normal", box);
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = AccentC;
        e.AddThemeStyleboxOverride("focus", focus);
        e.AddThemeColorOverride("font_color", TextC);
        e.AddThemeColorOverride("font_placeholder_color", TextDimC);
        e.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
    }

    private static void StyleOptionButton(OptionButton b)
    {
        StsFonts.ApplyTo(b);
        var box = new StyleBoxFlat
        {
            BgColor = InputBgC, BorderColor = BorderC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", box);
        b.AddThemeColorOverride("font_color", TextC);
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontCaption);
    }

    // A tiny × delete badge designed to overlay on top of another button.
    // Normal state: fully transparent (no border, no fill, just the × glyph).
    // Hover: red fill + light text, signalling destructive action.
    private static Button MakeDeleteBadge()
    {
        var b = new Button { Text = "×" };
        StsFonts.ApplyTo(b, StsFonts.FontWeight.Bold);
        var transparent = new StyleBoxEmpty();
        var hover = new StyleBoxFlat
        {
            BgColor = StsTheme.MenuRedAccent,
            BorderColor = StsTheme.MenuRedAccent,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 0, ContentMarginRight = 0,
            ContentMarginTop = 0, ContentMarginBottom = 0,
        };
        b.AddThemeStyleboxOverride("normal", transparent);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", transparent);
        b.AddThemeColorOverride("font_color", TextDimC);
        b.AddThemeColorOverride("font_hover_color", new Color("FFFFFF"));
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
        return b;
    }

    private static Button MakeButton(string text, Color normal, Color hover)
    {
        var b = new Button { Text = text };
        StsFonts.ApplyTo(b);
        var nrm = new StyleBoxFlat
        {
            BgColor = normal, BorderColor = BorderC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        var hov = new StyleBoxFlat
        {
            BgColor = hover, BorderColor = AccentC,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        b.AddThemeStyleboxOverride("normal", nrm);
        b.AddThemeStyleboxOverride("hover", hov);
        b.AddThemeStyleboxOverride("pressed", hov);
        b.AddThemeStyleboxOverride("focus", nrm);
        b.AddThemeColorOverride("font_color", TextC);
        b.AddThemeColorOverride("font_hover_color", AccentC);
        b.AddThemeFontSizeOverride("font_size", StsTheme.FontCaption);
        return b;
    }
}
