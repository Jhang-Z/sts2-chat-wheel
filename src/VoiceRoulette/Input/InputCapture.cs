using Godot;
using System;
using VoiceRoulette.UI;

namespace VoiceRoulette.Input;

public sealed partial class InputCapture : Node
{
    private readonly Key _hotkey;
    private readonly WheelUI _wheel;
    private bool _held;
    private Vector2 _origin;

    public event Action<int>? Released; // sector idx, or -1 if cancelled

    public InputCapture(Key hotkey, WheelUI wheel)
    {
        _hotkey = hotkey;
        _wheel = wheel;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Keycode == _hotkey)
        {
            if (k.Pressed && !_held)
            {
                _held = true;
                _origin = GetViewport().GetMousePosition();
                _wheel.OpenWheel(GetCurrentLineTexts());
            }
            else if (!k.Pressed && _held)
            {
                _held = false;
                Released?.Invoke(_wheel.SelectedIndex);
                _wheel.CloseWheel();
            }
        }
        else if (ev is InputEventKey esc && esc.Pressed && esc.Keycode == Key.Escape && _held)
        {
            _held = false;
            Released?.Invoke(-1);
            _wheel.CloseWheel();
        }
        else if (ev is InputEventMouseMotion mm && _held)
        {
            _wheel.SetSelectedFromMouse(GetViewport().GetMousePosition() - _origin);
        }
    }

    // Replaced by binding to LineRegistry in Plugin.cs wiring (Task 15).
    private string[] GetCurrentLineTexts() => Array.Empty<string>();
}
