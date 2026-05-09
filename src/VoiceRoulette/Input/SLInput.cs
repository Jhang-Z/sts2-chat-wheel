using System;
using Godot;

namespace VoiceRoulette.Input;

/// <summary>
/// Polls for the SL hotkey (default Cmd/Ctrl+Shift+R) and Y/N keys while a
/// vote prompt is active. The hotkey is edge-triggered; Y/N also edge-triggered
/// so holding them doesn't spam.
/// </summary>
public sealed partial class SLInput : Node
{
    private SceneTree? _tree;
    private Action? _onTrigger;
    private Action? _onAccept;
    private Action? _onVeto;
    private Func<bool>? _isPromptActive;

    private Key _hotkey = Key.R;
    private bool _previousTrigger;
    private bool _previousAccept;
    private bool _previousVeto;

    public void Start(
        Key hotkey,
        Action onTrigger,
        Action onAccept,
        Action onVeto,
        Func<bool> isPromptActive)
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _hotkey = hotkey;
        _onTrigger = onTrigger;
        _onAccept = onAccept;
        _onVeto = onVeto;
        _isPromptActive = isPromptActive;
        _tree.ProcessFrame += OnTick;
        GD.Print($"[VR][SLInput] started — hotkey=Cmd/Ctrl+Shift+{hotkey} (Enter accept / Esc veto while prompt active)");
    }

    public void Rebind(Key hotkey)
    {
        _hotkey = hotkey;
        GD.Print($"[VR][SLInput] rebound hotkey to Cmd/Ctrl+Shift+{hotkey}");
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.ProcessFrame -= OnTick;
    }

    private void OnTick()
    {
        // Trigger requires Cmd OR Ctrl + Shift + main key (all currently down).
        var modBase = Godot.Input.IsKeyPressed(Key.Meta) || Godot.Input.IsKeyPressed(Key.Ctrl);
        var shift   = Godot.Input.IsKeyPressed(Key.Shift);
        var main    = Godot.Input.IsKeyPressed(_hotkey);
        var trigDown = modBase && shift && main;
        if (trigDown && !_previousTrigger) _onTrigger?.Invoke();
        _previousTrigger = trigDown;

        // Y / N keys only consumed while the prompt is showing.
        var promptActive = _isPromptActive?.Invoke() == true;

        // Use Enter/Escape rather than Y/N to avoid colliding with the wheel
        // hotkey (default Y) and other gameplay inputs.
        var acceptDown = promptActive && (Godot.Input.IsKeyPressed(Key.Enter) || Godot.Input.IsKeyPressed(Key.KpEnter));
        if (acceptDown && !_previousAccept) _onAccept?.Invoke();
        _previousAccept = acceptDown;

        var vetoDown = promptActive && Godot.Input.IsKeyPressed(Key.Escape);
        if (vetoDown && !_previousVeto) _onVeto?.Invoke();
        _previousVeto = vetoDown;
    }
}
