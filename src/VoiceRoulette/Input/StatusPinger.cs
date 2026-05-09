using Godot;
using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace VoiceRoulette.Input;

// Unified Cmd/Ctrl + Right-click handler. Dispatches to one of:
//   • enemy under cursor   → onMark(enemyPos) — drops a tactical marker
//   • potion               → "我有【火焰药水】"
//   • power                → "我处于【力量2】的状态"
//   • relic                → "我有遗物【燃烧之血】"
//   • top-bar gold         → "我有 150 金币"
//   • top-bar HP           → "我血量 75/80"
//   • combat energy        → "我能量 3/3"
//
// Priority order matters: enemy is checked first because the bottom-of-screen
// hit areas (potion belt, gold/hp on the left) would otherwise overlap with
// large enemies that extend low.
public sealed partial class StatusPinger : Node
{
    private SceneTree? _tree;
    private Action<string>? _onPing;
    private Action<Vector2>? _onMark;
    private bool _previousRightPressed;

    public void Start(Action<string> onPing, Action<Vector2> onMark)
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _onPing = onPing;
        _onMark = onMark;
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][Pinger] Start — Cmd/Ctrl+Right-click on enemy/potion/power/relic/gold/hp/energy");
    }

    private void OnTick()
    {
        if (_tree == null || _onPing == null || _onMark == null) return;

        var mousePos = _tree.Root.GetMousePosition();

        var modifierHeld =
            Godot.Input.IsKeyPressed(Key.Meta) ||
            Godot.Input.IsKeyPressed(Key.Ctrl);

        var rightPressed = Godot.Input.IsMouseButtonPressed(MouseButton.Right);
        var clicked = rightPressed && !_previousRightPressed && modifierHeld;
        _previousRightPressed = rightPressed;

        if (!clicked) return;

        // Single tree walk that gathers all candidate hits in priority order.
        var hit = FindUnderCursor(_tree.Root, mousePos);
        switch (hit.Kind)
        {
            case HitKind.Enemy:
                GD.Print($"[VR][Pinger] enemy mark at {hit.EnemyPos}");
                _onMark(hit.EnemyPos);
                break;
            case HitKind.Potion:
                Ping($"我有【{NameOrUnknown(hit.Potion!.Model?.Title?.GetFormattedText())}】");
                break;
            case HitKind.Power:
                {
                    var name = NameOrUnknown(hit.Power!.Model?.Title?.GetFormattedText());
                    var amount = hit.Power.Model?.DisplayAmount ?? 0;
                    Ping(amount != 0 ? $"我处于【{name}{amount}】的状态" : $"我处于【{name}】状态");
                    break;
                }
            case HitKind.Relic:
                Ping($"我有遗物【{NameOrUnknown(hit.Relic!.Model?.Title?.GetFormattedText())}】");
                break;
            case HitKind.Gold:
                Ping($"我有 {hit.IntValue} 金币");
                break;
            case HitKind.Hp:
                Ping($"我血量 {hit.IntValue}/{hit.IntValue2}");
                break;
            case HitKind.Energy:
                Ping($"我能量 {hit.IntValue}/{hit.IntValue2}");
                break;
            default:
                GD.Print($"[VR][Pinger] no actionable target under cursor at {mousePos}");
                break;
        }
    }

    private void Ping(string msg)
    {
        GD.Print($"[VR][Pinger] {msg}");
        _onPing?.Invoke(msg);
    }

    private static string NameOrUnknown(string? s) => string.IsNullOrWhiteSpace(s) ? "未知" : s;

    // ── Hit detection ────────────────────────────────────────────────────────

    private enum HitKind { None, Enemy, Potion, Power, Relic, Gold, Hp, Energy }

    private struct HitResult
    {
        public HitKind Kind;
        public Vector2 EnemyPos;
        public NPotion? Potion;
        public NPower?  Power;
        public NRelic?  Relic;
        public int IntValue;   // gold, current hp, current energy
        public int IntValue2;  // max hp, max energy
    }

    private static HitResult FindUnderCursor(Node root, Vector2 mousePos)
    {
        // Best-of-class trackers; we pick winner by priority at the end.
        Vector2? bestEnemy = null;        float bestEnemyDist = float.MaxValue;
        NPotion? bestPotion = null;       float bestPotionDist = float.MaxValue;
        NPower?  bestPower  = null;       float bestPowerDist  = float.MaxValue;
        NRelic?  bestRelic  = null;       float bestRelicDist  = float.MaxValue;
        Control? goldHit    = null;
        Control? hpHit      = null;
        Control? energyHit  = null;

        Walk(root);

        // Priority: enemy > combat-area items (potion/power/relic) > top-bar (gold/hp/energy).
        if (bestEnemy.HasValue)
            return new HitResult { Kind = HitKind.Enemy, EnemyPos = bestEnemy.Value };
        if (bestPotion != null) return new HitResult { Kind = HitKind.Potion, Potion = bestPotion };
        if (bestPower  != null) return new HitResult { Kind = HitKind.Power,  Power = bestPower  };
        if (bestRelic  != null) return new HitResult { Kind = HitKind.Relic,  Relic = bestRelic  };

        if (goldHit != null && TryReadGold(goldHit, out var gold))
            return new HitResult { Kind = HitKind.Gold, IntValue = gold };
        if (hpHit != null && TryReadHp(hpHit, out var cur, out var max))
            return new HitResult { Kind = HitKind.Hp, IntValue = cur, IntValue2 = max };
        if (energyHit != null && TryReadEnergy(energyHit, out var ce, out var me))
            return new HitResult { Kind = HitKind.Energy, IntValue = ce, IntValue2 = me };

        return default;

        void Walk(Node n)
        {
            // Enemy creature
            if (n is NCreature nc && nc.IsVisibleInTree())
            {
                var creature = nc.Entity;
                if (creature?.IsEnemy == true && creature.IsAlive)
                {
                    var rect = nc.Hitbox?.GetGlobalRect()
                        ?? new Rect2(nc.GlobalPosition - new Vector2(80, 140), new Vector2(160, 180));
                    if (rect.HasPoint(mousePos))
                    {
                        var d = (nc.GlobalPosition - mousePos).Length();
                        if (d < bestEnemyDist) { bestEnemyDist = d; bestEnemy = nc.GlobalPosition; }
                    }
                }
            }
            else if (n is NPotion pot && pot.IsVisibleInTree())
            {
                if (pot.GetGlobalRect().HasPoint(mousePos))
                {
                    var d = (pot.GetGlobalRect().GetCenter() - mousePos).Length();
                    if (d < bestPotionDist) { bestPotionDist = d; bestPotion = pot; }
                }
            }
            else if (n is NPower pow && pow.IsVisibleInTree())
            {
                if (pow.GetGlobalRect().HasPoint(mousePos))
                {
                    var d = (pow.GetGlobalRect().GetCenter() - mousePos).Length();
                    if (d < bestPowerDist) { bestPowerDist = d; bestPower = pow; }
                }
            }
            else if (n is NRelic rel && rel.IsVisibleInTree())
            {
                if (rel.GetGlobalRect().HasPoint(mousePos))
                {
                    var d = (rel.GetGlobalRect().GetCenter() - mousePos).Length();
                    if (d < bestRelicDist) { bestRelicDist = d; bestRelic = rel; }
                }
            }
            else if (n is Control ctrl && ctrl.IsVisibleInTree())
            {
                // Match by type name string so we don't have to add a using
                // for a deeper game-internal namespace just for this.
                var typeName = ctrl.GetType().Name;
                if (typeName == "NTopBarGold" && ctrl.GetGlobalRect().HasPoint(mousePos)) goldHit = ctrl;
                else if (typeName == "NTopBarHp" && ctrl.GetGlobalRect().HasPoint(mousePos)) hpHit = ctrl;
                else if (typeName == "NEnergyCounter" && ctrl.GetGlobalRect().HasPoint(mousePos)) energyHit = ctrl;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    // ── Reflection: pull values from the game's runtime state ─────────────

    private const BindingFlags PrivInst = BindingFlags.NonPublic | BindingFlags.Instance;

    private static bool TryReadGold(Control gold, out int amount)
    {
        amount = 0;
        try
        {
            var playerField = gold.GetType().GetField("_player", PrivInst);
            var player = playerField?.GetValue(gold);
            var goldProp = player?.GetType().GetProperty("Gold");
            if (goldProp?.GetValue(player) is int g) { amount = g; return true; }
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] gold read fail: {ex.Message}"); }
        return false;
    }

    private static bool TryReadHp(Control hp, out int current, out int max)
    {
        current = 0; max = 0;
        try
        {
            var playerField = hp.GetType().GetField("_player", PrivInst);
            var player = playerField?.GetValue(hp);
            var creatureProp = player?.GetType().GetProperty("Creature");
            var creature = creatureProp?.GetValue(player);
            if (creature == null) return false;
            var ct = creature.GetType();
            var cur = ct.GetProperty("CurrentHp")?.GetValue(creature);
            var mx  = ct.GetProperty("MaxHp")?.GetValue(creature);
            if (cur is int ci && mx is int mi) { current = ci; max = mi; return true; }
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] hp read fail: {ex.Message}"); }
        return false;
    }

    private static bool TryReadEnergy(Control energy, out int current, out int max)
    {
        current = 0; max = 0;
        try
        {
            var playerField = energy.GetType().GetField("_player", PrivInst);
            var player = playerField?.GetValue(energy);
            if (player == null) return false;
            var pt = player.GetType();
            // Current energy lives on PlayerCombatState; max on Player itself.
            var maxProp = pt.GetProperty("MaxEnergy");
            if (maxProp?.GetValue(player) is int m) max = m;
            var pcsProp = pt.GetProperty("PlayerCombatState");
            var pcs = pcsProp?.GetValue(player);
            if (pcs != null)
            {
                // PlayerCombatState exposes Energy or CurrentEnergy — try both.
                foreach (var name in new[] { "CurrentEnergy", "Energy" })
                {
                    var v = pcs.GetType().GetProperty(name)?.GetValue(pcs);
                    if (v is int e) { current = e; return true; }
                }
            }
            return max != 0;  // at least we have max
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] energy read fail: {ex.Message}"); }
        return false;
    }
}
