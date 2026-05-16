using System;
using Godot;

namespace VoiceRoulette.UI;

/// <summary>
/// Always-on screen button to trigger the SL flow. Replaces the previous
/// hotkey-based trigger so the feature is discoverable to teammates who
/// don't know the key combo.
/// </summary>
public sealed partial class SLTriggerButton : CanvasLayer
{
    private const int LayerIndex = 95;
    private Button? _button;
    public Action? OnPressed;

    public void Start()
    {
        Layer = LayerIndex;

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
    }
}
