using Godot;
using System;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace VoiceRoulette.Input;

/// <summary>
/// Hold F + click an enemy → broadcast a tactical marker on that enemy.
/// Clicks that don't hit an enemy creature are ignored — empty-space marks
/// were noisy and confusing. The mark anchors to the enemy's NCreature
/// global position (above the body).
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
        GD.Print("[VR][MarkerInput] Start — Hold F + click an enemy to mark target");
    }

    private void OnTick()
    {
        if (_tree == null || _onMark == null) return;

        var modifierHeld = Godot.Input.IsKeyPressed(Key.F);
        var leftPressed  = Godot.Input.IsMouseButtonPressed(MouseButton.Left);
        var clicked = leftPressed && !_previousLeftPressed && modifierHeld;
        _previousLeftPressed = leftPressed;

        if (!clicked) return;

        var mousePos = _tree.Root.GetMousePosition();
        var enemyPos = FindEnemyUnderCursor(_tree.Root, mousePos);
        if (enemyPos is null)
        {
            GD.Print($"[VR][MarkerInput] no enemy under cursor at {mousePos} — ignored");
            return;
        }

        GD.Print($"[VR][MarkerInput] mark enemy at {enemyPos}");
        _onMark(enemyPos.Value);
    }

    /// <summary>
    /// Walk the tree looking for an NCreature (enemy side) whose hitbox
    /// contains the cursor. Skips player creatures so F+click on yourself
    /// or a teammate doesn't fire a marker. Returns the creature's GlobalPosition
    /// if hit, null otherwise.
    /// </summary>
    private static Vector2? FindEnemyUnderCursor(Node root, Vector2 mousePos)
    {
        Vector2? best = null;
        float bestDist = float.MaxValue;

        Walk(root);
        return best;

        void Walk(Node n)
        {
            if (n is NCreature nc && nc.IsVisibleInTree())
            {
                // Filter to enemies only — F+click on yourself / teammates
                // shouldn't fire a "都打这个" call-out.
                var creature = nc.Entity;
                if (creature?.IsEnemy == true && creature.IsAlive)
                {
                    // Use the engine's own Hitbox control for accurate click
                    // detection when available; fall back to a bounding box
                    // around GlobalPosition.
                    var hit = false;
                    var pos = nc.GlobalPosition;
                    if (nc.Hitbox != null)
                    {
                        hit = nc.Hitbox.GetGlobalRect().HasPoint(mousePos);
                    }
                    else
                    {
                        var rect = new Rect2(pos - new Vector2(80, 140), new Vector2(160, 180));
                        hit = rect.HasPoint(mousePos);
                    }
                    if (hit)
                    {
                        var d = (pos - mousePos).Length();
                        if (d < bestDist) { bestDist = d; best = pos; }
                    }
                }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }
}
