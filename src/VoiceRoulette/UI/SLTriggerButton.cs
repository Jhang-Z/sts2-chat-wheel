using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace VoiceRoulette.UI;

/// <summary>
/// Always-on screen button to trigger the SL flow. Replaces the previous
/// hotkey-based trigger so the feature is discoverable to teammates who
/// don't know the key combo. Auto-hides outside of an active run (main
/// menu, loading screens, between runs) so it doesn't clutter the UI.
/// </summary>
public sealed partial class SLTriggerButton : CanvasLayer
{
    private const int LayerIndex = 95;
    private const double VisibilityCheckIntervalSec = 0.25;

    private Button? _button;
    private SceneTree? _tree;
    private double _nextCheckSec;

    public Action? OnPressed;

    public void Start()
    {
        Layer = LayerIndex;
        _tree = (SceneTree)Engine.GetMainLoop();

        var root = new Control
        {
            AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(root);

        _button = new Button
        {
            Text = "发起 SL",
            CustomMinimumSize = new Vector2(120, 40),
            MouseFilter = Control.MouseFilterEnum.Stop,
            // Anchor to top-right corner, with margin from edges.
            AnchorLeft = 1, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 0,
            OffsetLeft = -150, OffsetRight = -30,
            OffsetTop = 210, OffsetBottom = 250,
            Visible = false,  // hidden until we detect an active run
        };
        _button.AddThemeStyleboxOverride("normal",
            StsTheme.Button(StsTheme.BtnPrimaryBg, StsTheme.BtnPrimaryHover));
        _button.AddThemeStyleboxOverride("hover",
            StsTheme.Button(StsTheme.BtnPrimaryHover, StsTheme.Gold));
        _button.AddThemeStyleboxOverride("pressed",
            StsTheme.Button(StsTheme.BtnPrimaryHover, StsTheme.Gold));
        _button.AddThemeColorOverride("font_color", StsTheme.Cream);
        _button.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
        _button.Pressed += () => OnPressed?.Invoke();
        root.AddChild(_button);

        _tree.ProcessFrame += OnTick;
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.ProcessFrame -= OnTick;
    }

    private void OnTick()
    {
        if (_button == null) return;
        var nowSec = Time.GetTicksMsec() / 1000.0;
        if (nowSec < _nextCheckSec) return;
        _nextCheckSec = nowSec + VisibilityCheckIntervalSec;

        // Show only when a run is actually in progress (combat or map nav).
        // Hides at main menu, loading screens, between-run transitions.
        _button.Visible = IsRunInProgress();
    }

    private static bool IsRunInProgress()
    {
        try
        {
            var rm = RunManager.Instance;
            if (rm == null) return false;
            var prop = rm.GetType().GetProperty("IsInProgress",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(rm) is bool b && b;
        }
        catch { return false; }
    }
}
