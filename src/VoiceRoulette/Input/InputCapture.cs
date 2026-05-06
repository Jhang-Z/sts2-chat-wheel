using Godot;
using System;
using System.Collections.Generic;
using VoiceRoulette.UI;

namespace VoiceRoulette.Input;

// NOTE: This mod project uses Microsoft.NET.Sdk (not Godot.NET.Sdk), so the Godot
// source generators do NOT run. Engine callbacks like _Ready / _Input declared as
// overrides are never wired up. We poll via SceneTree signals + the Input singleton.
public sealed partial class InputCapture : Node
{
    private readonly Key _hotkey;
    private readonly Key _settingsHotkey;
    private readonly WheelUI _wheel;
    private readonly Func<IList<string>> _getLineTexts;
    private bool _held;
    private bool _previousPressed;
    private bool _previousSettingsPressed;
    private Vector2 _origin;
    private SceneTree? _tree;

    public event Action<int>? Released;
    public event Action? SettingsToggled;

    public InputCapture(Key hotkey, WheelUI wheel, Func<IList<string>>? getLineTexts = null, Key settingsHotkey = Key.Semicolon)
    {
        _hotkey = hotkey;
        _settingsHotkey = settingsHotkey;
        _wheel = wheel;
        _getLineTexts = getLineTexts ?? (() => Array.Empty<string>());
    }

    /// <summary>
    /// Manual lifecycle hook — call from Plugin after AddChild succeeds.
    /// We can't rely on Godot calling _Ready since source generators are absent.
    /// </summary>
    public void StartPolling()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _tree.ProcessFrame += OnProcessFrame;
        GD.Print("[VR][Input] StartPolling: hooked SceneTree.ProcessFrame");
    }

    private void OnProcessFrame()
    {
        // Settings hotkey toggles the settings screen (edge-detected).
        // Default ';' to avoid macOS F-key brightness/volume conflict.
        var settingsPressed = Godot.Input.IsKeyPressed(_settingsHotkey);
        if (settingsPressed && !_previousSettingsPressed) SettingsToggled?.Invoke();
        _previousSettingsPressed = settingsPressed;

        // Poll the Input singleton instead of overriding _Input.
        var pressed = Godot.Input.IsKeyPressed(_hotkey);
        var escPressed = Godot.Input.IsKeyPressed(Key.Escape);

        if (pressed && !_previousPressed && !_held)
        {
            // Edge: just pressed
            GD.Print("[VR][Input] V pressed -> opening wheel");
            _held = true;
            _origin = GetViewport().GetMousePosition();
            _wheel.OpenWheel(_getLineTexts());
        }
        else if (!pressed && _previousPressed && _held)
        {
            // Edge: just released
            GD.Print($"[VR][Input] V released -> sector={_wheel.SelectedIndex}");
            _held = false;
            Released?.Invoke(_wheel.SelectedIndex);
            _wheel.CloseWheel();
        }
        else if (escPressed && _held)
        {
            GD.Print("[VR][Input] Esc cancel");
            _held = false;
            Released?.Invoke(-1);
            _wheel.CloseWheel();
        }
        else if (_held)
        {
            // Direction relative to the WHEEL CENTER (screen center), not press point.
            // Mouse position over a sector picks that sector directly.
            var screenCenter = GetViewport().GetVisibleRect().Size / 2f;
            var delta = GetViewport().GetMousePosition() - screenCenter;
            _wheel.SetSelectedFromMouse(delta);
        }

        _previousPressed = pressed;
    }
}
