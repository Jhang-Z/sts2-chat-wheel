using Godot;
using System;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace VoiceRoulette.Input;

// Cmd+Click (Mac) / Ctrl+Click (Win/Linux) on a potion / power / relic broadcasts
//   "我有【能量药水】" / "我处于【力量2】的状态" / "我有遗物【燃烧之血】"
// Names come directly from the game's Model objects — no tooltip scraping needed.
public sealed partial class StatusPinger : Node
{
    private SceneTree? _tree;
    private Action<string>? _onPing;
    private bool _previousLeftPressed;

    public void Start(Action<string> onPing)
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _onPing = onPing;
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][Pinger] Start — Cmd/Ctrl+Click potion/power");
    }

    private void OnTick()
    {
        if (_tree == null || _onPing == null) return;

        var mousePos = _tree.Root.GetMousePosition();

        // ── Cmd/Ctrl+left-click → announce potion or power ───────────────────
        var modifierHeld =
            Godot.Input.IsKeyPressed(Key.Meta) ||
            Godot.Input.IsKeyPressed(Key.Ctrl);

        var leftPressed = Godot.Input.IsMouseButtonPressed(MouseButton.Left);
        var clicked = leftPressed && !_previousLeftPressed && modifierHeld;
        _previousLeftPressed = leftPressed;

        if (!clicked) return;

        var (potion, power, relic) = FindUnderCursor(_tree.Root, mousePos);

        if (potion != null)
        {
            var name = potion.Model?.Title?.GetFormattedText();
            if (string.IsNullOrWhiteSpace(name)) name = "未知";
            var msg = $"我有【{name}】";
            GD.Print($"[VR][Pinger] potion ping: {msg}");
            _onPing(msg);
        }
        else if (power != null)
        {
            var name = power.Model?.Title?.GetFormattedText();
            if (string.IsNullOrWhiteSpace(name)) name = "未知";
            var amount = power.Model?.DisplayAmount ?? 0;
            var msg = amount != 0
                ? $"我处于【{name}{amount}】的状态"
                : $"我处于【{name}】状态";
            GD.Print($"[VR][Pinger] power ping: {msg}");
            _onPing(msg);
        }
        else if (relic != null)
        {
            var name = relic.Model?.Title?.GetFormattedText();
            if (string.IsNullOrWhiteSpace(name)) name = "未知";
            var msg = $"我有遗物【{name}】";
            GD.Print($"[VR][Pinger] relic ping: {msg}");
            _onPing(msg);
        }
        else
        {
            GD.Print($"[VR][Pinger] no potion/power/relic under cursor at {mousePos}");
        }
    }

    // ── Hit detection ────────────────────────────────────────────────────────

    private static (NPotion? potion, NPower? power, NRelic? relic) FindUnderCursor(Node root, Vector2 mousePos)
    {
        NPotion? bestPotion = null;
        NPower? bestPower = null;
        NRelic? bestRelic = null;
        float bestPotionDist = float.MaxValue;
        float bestPowerDist = float.MaxValue;
        float bestRelicDist = float.MaxValue;

        Walk(root);
        return (bestPotion, bestPower, bestRelic);

        void Walk(Node n)
        {
            if (n is NPotion pot && pot.IsVisibleInTree())
            {
                var rect = pot.GetGlobalRect();
                if (rect.HasPoint(mousePos))
                {
                    var dist = (rect.GetCenter() - mousePos).Length();
                    if (dist < bestPotionDist) { bestPotionDist = dist; bestPotion = pot; }
                }
            }
            else if (n is NPower pow && pow.IsVisibleInTree())
            {
                var rect = pow.GetGlobalRect();
                if (rect.HasPoint(mousePos))
                {
                    var dist = (rect.GetCenter() - mousePos).Length();
                    if (dist < bestPowerDist) { bestPowerDist = dist; bestPower = pow; }
                }
            }
            else if (n is NRelic rel && rel.IsVisibleInTree())
            {
                var rect = rel.GetGlobalRect();
                if (rect.HasPoint(mousePos))
                {
                    var dist = (rect.GetCenter() - mousePos).Length();
                    if (dist < bestRelicDist) { bestRelicDist = dist; bestRelic = rel; }
                }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

}
