using System;
using Godot;

namespace VoiceRoulette.UI;

/// <summary>
/// Centered prompt overlay for the SL flow:
///   "队友请求 SL — 同意 [Y]   反对 [N]    自动同意 X.X 秒"
/// Displayed for the duration of the vote window (default 10s); also used
/// as a transient toast for status messages ("已取消", "载入存档点…").
/// Layer 120 — above bubbles (which the existing BubbleOverlay uses ~100).
/// </summary>
public sealed partial class SLPromptOverlay : CanvasLayer
{
    private const int Layer120 = 120;

    private Control? _root;
    private PanelContainer? _panel;
    private Label? _title;
    private Label? _hint;
    private Label? _countdown;
    private Button? _confirmBtn;
    private Button? _vetoBtn;

    private SceneTree? _tree;

    public Action? OnConfirmClicked;
    public Action? OnVetoClicked;

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = Layer120;
        BuildUi();
        if (_root != null) _root.Visible = false;
    }

    private void BuildUi()
    {
        // Full-screen anchor so we can center inside it.
        _root = new Control
        {
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,  // don't eat clicks
        };
        AddChild(_root);

        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical   = Control.GrowDirection.Both,
            // Buttons need Stop so clicks register on them; the panel
            // background can pass through (we set per-control as needed).
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var sb = StsTheme.Panel(StsTheme.RadiusLarge, StsTheme.BorderHi);
        // Slightly larger content padding for breathing room.
        sb.ContentMarginLeft = 28; sb.ContentMarginRight = 28;
        sb.ContentMarginTop = 18;  sb.ContentMarginBottom = 18;
        _panel.AddThemeStyleboxOverride("panel", sb);
        _root.AddChild(_panel);

        var v = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        _panel.AddChild(v);

        _title = new Label
        {
            Text = "请求 SL",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _title.AddThemeColorOverride("font_color", StsTheme.MenuAccent);
        _title.AddThemeFontSizeOverride("font_size", StsTheme.FontH1);
        v.AddChild(_title);

        v.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 8) });

        _hint = new Label
        {
            Text = "[Enter] 同意    [Esc] 反对    自动同意倒计时",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _hint.AddThemeColorOverride("font_color", StsTheme.MenuText);
        _hint.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
        v.AddChild(_hint);

        _countdown = new Label
        {
            Text = "10.0",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _countdown.AddThemeColorOverride("font_color", StsTheme.Gold);
        _countdown.AddThemeFontSizeOverride("font_size", StsTheme.FontH2);
        v.AddChild(_countdown);

        v.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 8) });

        // Button row — clickable affordances. Keyboard (Enter / Esc) still
        // works in parallel via SLInput.
        var btnRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        v.AddChild(btnRow);

        _confirmBtn = new Button
        {
            Text = "立即执行 SL",
            CustomMinimumSize = new Vector2(180, 44),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _confirmBtn.AddThemeStyleboxOverride("normal", StsTheme.Button(StsTheme.BtnPrimaryBg, StsTheme.BtnPrimaryHover));
        _confirmBtn.AddThemeStyleboxOverride("hover",  StsTheme.Button(StsTheme.BtnPrimaryHover, StsTheme.Gold));
        _confirmBtn.AddThemeStyleboxOverride("pressed",StsTheme.Button(StsTheme.BtnPrimaryHover, StsTheme.Gold));
        _confirmBtn.AddThemeColorOverride("font_color", StsTheme.Cream);
        _confirmBtn.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
        _confirmBtn.Pressed += () => OnConfirmClicked?.Invoke();
        btnRow.AddChild(_confirmBtn);

        btnRow.AddChild(new Control { CustomMinimumSize = new Vector2(20, 0) });

        _vetoBtn = new Button
        {
            Text = "取消 / 反对",
            CustomMinimumSize = new Vector2(160, 44),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _vetoBtn.AddThemeStyleboxOverride("normal", StsTheme.Button(StsTheme.BtnBg, StsTheme.PanelBorder));
        _vetoBtn.AddThemeStyleboxOverride("hover",  StsTheme.Button(StsTheme.BtnBgHover, StsTheme.Gold));
        _vetoBtn.AddThemeStyleboxOverride("pressed",StsTheme.Button(StsTheme.BtnBgHover, StsTheme.Gold));
        _vetoBtn.AddThemeColorOverride("font_color", StsTheme.MenuText);
        _vetoBtn.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
        _vetoBtn.Pressed += () => OnVetoClicked?.Invoke();
        btnRow.AddChild(_vetoBtn);
    }

    /// <summary>
    /// Show the vote prompt with a live "X / N" accept counter.
    /// `byMe` controls the wording: proposer sees a softer message,
    /// non-proposer sees an active vote-call message.
    /// </summary>
    public void Show(bool byMe, int expectedPeerCount, int alreadyAccepted)
    {
        if (_root == null || _title == null) Start();
        if (_title != null)
            _title.Text = byMe
                ? "已发起 SL — 等待队友确认"
                : "队友请求 SL — 请确认";
        if (_hint != null)
            _hint.Text = "[立即执行 SL] 同意    [取消] 反对";
        SetCounter(alreadyAccepted, expectedPeerCount);
        if (_root != null) _root.Visible = true;
    }

    /// <summary>Update the "X / N" tally label.</summary>
    public void SetCounter(int accepted, int total)
    {
        if (_countdown == null) Start();
        if (_countdown != null)
            _countdown.Text = $"已确认 {accepted} / {total}";
    }

    public new void Hide()
    {
        if (_root != null) _root.Visible = false;
    }

    /// <summary>Replace the prompt with a transient status message, then hide.</summary>
    public void HideWithMessage(string message, double seconds)
    {
        if (_root == null) Start();
        if (_title != null) _title.Text = message;
        if (_hint != null) _hint.Text = "";
        if (_countdown != null) _countdown.Text = "";
        if (_root != null) _root.Visible = true;

        var t = _tree ??= (SceneTree)Engine.GetMainLoop();
        var timer = t.CreateTimer(seconds);
        timer.Timeout += () => { if (_root != null) _root.Visible = false; };
    }

    // (countdown logic removed — vote tally is event-driven)
}
