using Godot;
using System;
using System.Collections.Generic;
using VoiceRoulette.Config;
using VoiceRoulette.Lines;

namespace VoiceRoulette.UI;

/// <summary>
/// Dota-style settings screen for the voice wheel.
/// Flow: click a SLOT first to select it (highlight), then click a LIBRARY phrase
/// to write that phrase into the selected slot.
/// Library phrases that have prerendered audio show a ♪ marker; phrases without
/// audio show ○ and dimmer text.
/// </summary>
public sealed partial class SettingsScreen : CanvasLayer
{
    private const int SlotCount = 8;

    // Palette aligned with the mockup design doc
    private static readonly Color PanelBg = new("18120ae8");        // deep brown overlay
    private static readonly Color PanelBorder = new("d4a937");      // gold trim
    private static readonly Color SlotBg = new("2a1d10");           // dark parchment back
    private static readonly Color SlotBgSelected = new("c8a878");   // parchment fill (selected)
    private static readonly Color CategoryColor = new("ffc23a");    // bright gold heading
    private static readonly Color LineColor = new("e8d4a8");        // cream text
    private static readonly Color LineColorSelected = new("2a1d10");// dark text on parchment
    private static readonly Color AudioIconColor = new("ffd76a");   // gold for ♪
    private static readonly Color NoAudioColor = new("8a7858");     // muted brown for ○
    private static readonly Color SaveButtonBg = new("a4332c");     // red save button (mockup)
    private static readonly Color SaveButtonHover = new("c84a40");

    private const string IconAudio = "♪";
    private const string IconNoAudio = "○";

    private ConfigStore? _config;
    private Action? _onSaved;
    private Func<string, bool>? _hasAudio;
    private bool _initialized;
    private string _toggleKeyHint = ";";

    private readonly Button[] _slotButtons = new Button[SlotCount];
    private readonly List<(Button btn, string phrase)> _libraryButtons = new();
    private LineEdit? _customInput;
    private Label? _statusLabel;

    // State machine: slot is selected first; library click then writes into the selected slot.
    private int _selectedSlot = -1;

    public bool IsOpen => Visible;

    public void Initialize(
        ConfigStore config,
        Action onSaved,
        string toggleKeyHint = ";",
        Func<string, bool>? hasAudio = null)
    {
        if (_initialized) return;
        _initialized = true;
        _config = config;
        _onSaved = onSaved;
        _toggleKeyHint = toggleKeyHint;
        _hasAudio = hasAudio ?? (_ => false);
        Layer = 250;
        BuildUi();
        Visible = false;
        GD.Print("[VR][Settings] Initialize done");
    }

    public void Toggle()
    {
        if (!_initialized || _config == null) return;
        Visible = !Visible;
        if (Visible)
        {
            _selectedSlot = -1;
            RefreshSlotButtons();
            UpdateStatus("提示: 先点击左侧的槽位选中，然后点击右侧的台词进行替换。");
        }
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var panelSize = new Vector2(900, 620);
        var panelPos = (viewport - panelSize) / 2f;

        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            Size = viewport,
        };
        AddChild(bg);

        var panel = new Panel { Size = panelSize, Position = panelPos };
        var style = new StyleBoxFlat
        {
            BgColor = PanelBg,
            BorderColor = PanelBorder,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            BorderWidthLeft = 3,
            BorderWidthRight = 3,
            BorderWidthTop = 3,
            BorderWidthBottom = 3,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var title = new Label
        {
            Text = "语音轮盘 — 自定义",
            Position = new Vector2(0, 18),
            Size = new Vector2(panelSize.X, 32),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", CategoryColor);
        title.AddThemeFontSizeOverride("font_size", 24);
        panel.AddChild(title);

        var closeHint = new Label
        {
            Text = $"按 {_toggleKeyHint} 关闭",
            Position = new Vector2(panelSize.X - 160, 22),
            Size = new Vector2(140, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeHint.AddThemeColorOverride("font_color", LineColor);
        closeHint.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(closeHint);

        // Legend explaining the audio markers.
        var legend = new Label
        {
            Text = $"{IconAudio} 有语音    {IconNoAudio} 仅文字",
            Position = new Vector2(40, 50),
            Size = new Vector2(380, 20),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        legend.AddThemeColorOverride("font_color", LineColor);
        legend.AddThemeFontSizeOverride("font_size", 13);
        panel.AddChild(legend);

        BuildSlotsColumn(panel, new Vector2(40, 80), new Vector2(380, 460));
        BuildLibraryColumn(panel, new Vector2(450, 70), new Vector2(410, 420));
        BuildCustomRow(panel, new Vector2(450, 510), new Vector2(410, 40));

        var save = new Button
        {
            Text = "保存并应用",
            Position = new Vector2(40, 555),
            Size = new Vector2(160, 40),
        };
        save.Pressed += OnSavePressed;
        StyleSaveButton(save);
        panel.AddChild(save);

        var reset = new Button
        {
            Text = "恢复默认",
            Position = new Vector2(220, 555),
            Size = new Vector2(140, 40),
        };
        reset.Pressed += OnResetPressed;
        StyleButton(reset, false);
        panel.AddChild(reset);

        _statusLabel = new Label
        {
            Position = new Vector2(380, 565),
            Size = new Vector2(panelSize.X - 400, 30),
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _statusLabel.AddThemeColorOverride("font_color", LineColor);
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(_statusLabel);
    }

    private void BuildSlotsColumn(Control parent, Vector2 origin, Vector2 size)
    {
        var header = new Label
        {
            Text = "当前轮盘 (8 槽位) — 点击选中",
            Position = origin,
            Size = new Vector2(size.X, 28),
        };
        header.AddThemeColorOverride("font_color", CategoryColor);
        header.AddThemeFontSizeOverride("font_size", 18);
        parent.AddChild(header);

        const int cols = 2;
        var cellSize = new Vector2((size.X - 20) / cols, 100);
        var cellGap = 12;

        for (int i = 0; i < SlotCount; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var pos = origin + new Vector2(
                col * (cellSize.X + cellGap),
                40 + row * (cellSize.Y + cellGap));

            var btn = new Button
            {
                Text = $"#{i + 1}\n(空)",
                Position = pos,
                Size = cellSize,
                ClipText = true,
            };
            int idx = i;
            btn.Pressed += () => OnSlotClicked(idx);
            StyleButton(btn, false);
            parent.AddChild(btn);
            _slotButtons[i] = btn;
        }
    }

    private void BuildLibraryColumn(Control parent, Vector2 origin, Vector2 size)
    {
        var header = new Label
        {
            Text = "可选台词库 — 点击替换",
            Position = origin,
            Size = new Vector2(size.X, 28),
        };
        header.AddThemeColorOverride("font_color", CategoryColor);
        header.AddThemeFontSizeOverride("font_size", 18);
        parent.AddChild(header);

        var scroll = new ScrollContainer
        {
            Position = origin + new Vector2(0, 36),
            Size = size,
        };
        parent.AddChild(scroll);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(size.X - 16, 0),
        };
        vbox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(vbox);

        foreach (var category in LineLibrary.All)
        {
            var catLbl = new Label { Text = $"— {category.Name} —" };
            catLbl.AddThemeColorOverride("font_color", CategoryColor);
            catLbl.AddThemeFontSizeOverride("font_size", 16);
            vbox.AddChild(catLbl);

            var grid = new HFlowContainer();
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 6);
            vbox.AddChild(grid);

            foreach (var phrase in category.Phrases)
            {
                var btn = new Button
                {
                    CustomMinimumSize = new Vector2(110, 36),
                };
                StyleLibraryButton(btn, phrase, selected: false);
                string captured = phrase;
                btn.Pressed += () => OnLibraryClicked(captured);
                _libraryButtons.Add((btn, phrase));
                grid.AddChild(btn);
            }
        }
    }

    private void BuildCustomRow(Control parent, Vector2 origin, Vector2 size)
    {
        _customInput = new LineEdit
        {
            PlaceholderText = "输入自定义台词…",
            Position = origin,
            Size = new Vector2(size.X - 100, size.Y),
        };
        parent.AddChild(_customInput);

        var addBtn = new Button
        {
            Text = "写入选中槽位",
            Position = new Vector2(origin.X + size.X - 90, origin.Y),
            Size = new Vector2(90, size.Y),
        };
        addBtn.Pressed += OnAddCustomPressed;
        StyleButton(addBtn, true);
        parent.AddChild(addBtn);
    }

    // -------------------------------------------------------------------------
    // Interaction handlers
    // -------------------------------------------------------------------------

    private void OnSlotClicked(int idx)
    {
        if (_config == null) return;

        // Toggle off if same slot clicked twice
        if (_selectedSlot == idx)
        {
            _selectedSlot = -1;
            RefreshSlotButtons();
            UpdateStatus("已取消选中。");
            return;
        }

        // Slot-to-slot swap: if a slot was already selected, swap the two.
        if (_selectedSlot >= 0)
        {
            var a = EnsureSlotEntry(_selectedSlot);
            var b = EnsureSlotEntry(idx);
            (a.Text, b.Text) = (b.Text, a.Text);
            UpdateStatus($"已交换槽位 #{_selectedSlot + 1} ↔ #{idx + 1}");
            _selectedSlot = -1;
            RefreshSlotButtons();
            return;
        }

        // First click: select this slot, waiting for a library phrase.
        _selectedSlot = idx;
        var existing = _config.Schema.Pages.Common[idx].Text;
        UpdateStatus(string.IsNullOrEmpty(existing)
            ? $"已选中 #{idx + 1} (空) — 现在点右侧台词写入。"
            : $"已选中 #{idx + 1} ({existing}) — 现在点右侧台词替换。");
        RefreshSlotButtons();
    }

    private void OnLibraryClicked(string phrase)
    {
        if (_config == null) return;

        if (_selectedSlot < 0)
        {
            UpdateStatus("请先点击左侧任一槽位，然后再点这条台词。");
            return;
        }

        EnsureSlotEntry(_selectedSlot).Text = phrase;
        UpdateStatus($"#{_selectedSlot + 1} ← {phrase}");
        // Auto-advance: keep the same slot selected so user can keep iterating?
        // No — Dota deselects after a successful pick. Match that behaviour.
        _selectedSlot = -1;
        RefreshSlotButtons();
    }

    private void OnAddCustomPressed()
    {
        if (_customInput == null || _config == null) return;
        var text = _customInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            UpdateStatus("请输入文字");
            return;
        }
        if (_selectedSlot < 0)
        {
            UpdateStatus("请先选中一个槽位再点写入。");
            return;
        }
        EnsureSlotEntry(_selectedSlot).Text = text;
        UpdateStatus($"#{_selectedSlot + 1} ← {text}（自定义，无预渲染语音将走 Doubao TTS）");
        _selectedSlot = -1;
        _customInput.Text = "";
        RefreshSlotButtons();
    }

    private void OnSavePressed()
    {
        if (_config == null) return;
        _config.Save();
        _onSaved?.Invoke();
        UpdateStatus("已保存。");
        Visible = false;
    }

    private void OnResetPressed()
    {
        if (_config == null) return;
        _config.Schema.Pages = PagesConfig.Defaults();
        _selectedSlot = -1;
        RefreshSlotButtons();
        UpdateStatus("已重置为默认台词。");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private LineEntry EnsureSlotEntry(int idx)
    {
        if (_config == null) throw new InvalidOperationException("Config not bound");
        var list = _config.Schema.Pages.Common;
        while (list.Count <= idx)
            list.Add(new LineEntry { Id = $"slot_{list.Count}", Text = "" });
        return list[idx];
    }

    private void RefreshSlotButtons()
    {
        if (_config == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            var entry = i < _config.Schema.Pages.Common.Count ? _config.Schema.Pages.Common[i] : null;
            var text = entry?.Text;
            var hasAudio = !string.IsNullOrEmpty(text) && (_hasAudio?.Invoke(text!) ?? false);
            string display;
            if (string.IsNullOrEmpty(text))
                display = $"#{i + 1}\n(空)";
            else
                display = $"#{i + 1}  {(hasAudio ? IconAudio : IconNoAudio)}\n{text}";

            _slotButtons[i].Text = display;
            StyleButton(_slotButtons[i], highlighted: i == _selectedSlot);
        }
    }

    private void UpdateStatus(string msg)
    {
        if (_statusLabel != null) _statusLabel.Text = msg;
    }

    private void StyleLibraryButton(Button b, string phrase, bool selected)
    {
        var hasAudio = _hasAudio?.Invoke(phrase) ?? false;
        b.Text = $"{(hasAudio ? IconAudio : IconNoAudio)} {phrase}";

        var normal = new StyleBoxFlat
        {
            BgColor = selected ? SlotBgSelected : SlotBg,
            BorderColor = selected ? CategoryColor : new Color("46566e"),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", normal);
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("focus", normal);

        // Dim the whole button when there's no audio so it's visually obvious.
        var color = hasAudio ? LineColor : NoAudioColor;
        b.AddThemeColorOverride("font_color", color);
        b.AddThemeColorOverride("font_hover_color", LineColorSelected);
        b.AddThemeColorOverride("font_pressed_color", LineColorSelected);
        b.AddThemeFontSizeOverride("font_size", 15);
    }

    private static void StyleSaveButton(Button b)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = SaveButtonBg,
            BorderColor = new Color("d4a937"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        var hover = new StyleBoxFlat
        {
            BgColor = SaveButtonHover,
            BorderColor = new Color("ffd76a"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", normal);
        b.AddThemeColorOverride("font_color", new Color("ffe8d4"));
        b.AddThemeColorOverride("font_hover_color", new Color("ffffff"));
        b.AddThemeFontSizeOverride("font_size", 16);
    }

    private static void StyleButton(Button b, bool highlighted)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = highlighted ? SlotBgSelected : SlotBg,
            BorderColor = highlighted ? CategoryColor : new Color("46566e"),
            BorderWidthLeft = highlighted ? 3 : 2,
            BorderWidthRight = highlighted ? 3 : 2,
            BorderWidthTop = highlighted ? 3 : 2,
            BorderWidthBottom = highlighted ? 3 : 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", normal);
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("focus", normal);
        b.AddThemeColorOverride("font_color", highlighted ? LineColorSelected : LineColor);
        b.AddThemeColorOverride("font_hover_color", LineColorSelected);
        b.AddThemeColorOverride("font_pressed_color", LineColorSelected);
        b.AddThemeFontSizeOverride("font_size", 15);
    }
}
