using Godot;
using System;

namespace VoiceRoulette.Input;

/// <summary>
/// Hold F + click → broadcast a tactical marker at the cursor's world position.
/// Mirrors StatusPinger's modifier-key+click pattern. Picks up clicks on
/// anything (enemies, map nodes, potions, just empty space) — sender's
/// responsibility to point at something meaningful.
/// </summary>
public sealed partial class MarkerInput : Node
{
    private SceneTree? _tree;
    private Action<Vector2>? _onMark;
    private bool _previousLeftPressed;

    public void Start(Action<Vector2> onMark)
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _onMark = onMark;
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][MarkerInput] Start — Hold F + click to mark target");
    }

    private void OnTick()
    {
        if (_tree == null || _onMark == null) return;

        var modifierHeld = Godot.Input.IsKeyPressed(Key.F);
        var leftPressed  = Godot.Input.IsMouseButtonPressed(MouseButton.Left);
        var clicked = leftPressed && !_previousLeftPressed && modifierHeld;
        _previousLeftPressed = leftPressed;

        if (!clicked) return;

        // Use the global mouse pos so the marker matches what the sender
        // visually clicked on. Receivers will draw at the same world coord
        // because STS2's combat/map scenes are deterministic across peers.
        var worldPos = _tree.Root.GetMousePosition();
        GD.Print($"[VR][MarkerInput] mark at {worldPos}");
        _onMark(worldPos);
    }
}
