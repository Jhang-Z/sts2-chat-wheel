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

    private double _deadlineSec;
    private bool _counting;
    private SceneTree? _tree;

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
            MouseFilter = Control.MouseFilterEnum.Ignore,
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
    }

    /// <summary>
    /// Show the vote prompt for `seconds`. `byMe` controls the wording:
    /// proposer sees "已发起 SL 请求", others see "队友请求 SL".
    /// </summary>
    public void Show(bool byMe, double seconds)
    {
        if (_root == null || _title == null) Start();
        if (_title != null)
            _title.Text = byMe ? "已发起 SL — 队友可在倒计时内反对" : "队友请求 SL";
        if (_hint != null)
            _hint.Text = byMe
                ? "[Esc] 取消    倒计时结束后自动执行"
                : "[Enter] 同意    [Esc] 反对    超时视为同意";
        _deadlineSec = Time.GetTicksMsec() / 1000.0 + seconds;
        _counting = true;
        if (_root != null) _root.Visible = true;
        UpdateCountdown();
    }

    public new void Hide()
    {
        _counting = false;
        if (_root != null) _root.Visible = false;
    }

    /// <summary>Replace the prompt with a transient status message, then hide.</summary>
    public void HideWithMessage(string message, double seconds)
    {
        if (_root == null) Start();
        _counting = false;
        if (_title != null) _title.Text = message;
        if (_hint != null) _hint.Text = "";
        if (_countdown != null) _countdown.Text = "";
        if (_root != null) _root.Visible = true;

        var t = _tree ??= (SceneTree)Engine.GetMainLoop();
        var timer = t.CreateTimer(seconds);
        timer.Timeout += () => { if (_root != null) _root.Visible = false; };
    }

    public override void _Process(double delta)
    {
        if (!_counting) return;
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        if (_countdown == null) return;
        var nowSec = Time.GetTicksMsec() / 1000.0;
        var remaining = System.Math.Max(0.0, _deadlineSec - nowSec);
        _countdown.Text = remaining.ToString("F1");
    }
}
