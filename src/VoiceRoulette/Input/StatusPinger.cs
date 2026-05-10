using Godot;
using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace VoiceRoulette.Input;

// Unified Cmd/Ctrl + Right-click handler. Dispatches to one of:
//   • enemy under cursor   → onMark(pos, "都打这一只(血量 30/50)")
//   • potion               → "我有【火焰药水】"
//   • power                → "我处于【力量2】的状态"
//   • relic                → "我有遗物【燃烧之血】"
//   • top-bar gold         → "我有 150 金币"
//   • top-bar HP           → "我血量 75/80, 格挡 5"  (block included when > 0)
//   • combat energy        → "我能量 3/3"
//   • top-bar deck button  → "我牌组 32 张"
//   • draw pile button     → "抽牌堆 8 张"
//   • discard pile button  → "弃牌堆 3 张"
//   • exhaust pile button  → "消耗堆 2 张"
//
// Priority order matters: enemy is checked first because the bottom-of-screen
// hit areas (potion belt, gold/hp on the left) would otherwise overlap with
// large enemies that extend low.
public sealed partial class StatusPinger : Node
{
    private SceneTree? _tree;
    private Action<string>? _onPing;
    private Action<Vector2, string>? _onMark;
    private bool _previousRightPressed;

    public void Start(Action<string> onPing, Action<Vector2, string> onMark)
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
                {
                    var msg = hit.IntValue2 > 0
                        ? $"都打这一只(血量 {hit.IntValue}/{hit.IntValue2})"
                        : "都打这一只";
                    GD.Print($"[VR][Pinger] enemy mark at {hit.EnemyPos}: {msg}");
                    _onMark(hit.EnemyPos, msg);
                    break;
                }
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
                {
                    // IntValue=cur, IntValue2=max, Block stored in StringValue parsed back
                    var msg = $"我血量 {hit.IntValue}/{hit.IntValue2}";
                    if (hit.BlockValue > 0) msg += $", 格挡 {hit.BlockValue}";
                    Ping(msg);
                    break;
                }
            case HitKind.Energy:
                Ping($"我能量 {hit.IntValue}/{hit.IntValue2}");
                break;
            case HitKind.Deck:
                Ping($"我牌组 {hit.IntValue} 张");
                break;
            case HitKind.DrawPile:
                Ping($"抽牌堆 {hit.IntValue} 张");
                break;
            case HitKind.DiscardPile:
                Ping($"弃牌堆 {hit.IntValue} 张");
                break;
            case HitKind.ExhaustPile:
                Ping($"消耗堆 {hit.IntValue} 张");
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

    private enum HitKind { None, Enemy, Potion, Power, Relic, Gold, Hp, Energy, Deck, DrawPile, DiscardPile, ExhaustPile }

    private struct HitResult
    {
        public HitKind Kind;
        public Vector2 EnemyPos;
        public NPotion? Potion;
        public NPower?  Power;
        public NRelic?  Relic;
        public int IntValue;    // gold / current hp / current energy / deck count / pile count / enemy current hp
        public int IntValue2;   // max hp / max energy / enemy max hp
        public int BlockValue;  // player block (HP only)
    }

    private static HitResult FindUnderCursor(Node root, Vector2 mousePos)
    {
        // Best-of-class trackers; we pick winner by priority at the end.
        NCreature? bestEnemyNode = null;  float bestEnemyDist = float.MaxValue;
        NPotion? bestPotion = null;       float bestPotionDist = float.MaxValue;
        NPower?  bestPower  = null;       float bestPowerDist  = float.MaxValue;
        NRelic?  bestRelic  = null;       float bestRelicDist  = float.MaxValue;
        Control? goldHit    = null;
        Control? hpHit      = null;
        Control? energyHit  = null;
        Control? deckHit    = null;
        Control? drawHit    = null;
        Control? discardHit = null;
        Control? exhaustHit = null;

        Walk(root);

        // Priority: enemy > combat-area items > top-bar/UI buttons.
        if (bestEnemyNode != null)
        {
            var pos = bestEnemyNode.GlobalPosition;
            TryReadCreatureHp(bestEnemyNode, out var ehc, out var ehm);
            return new HitResult { Kind = HitKind.Enemy, EnemyPos = pos, IntValue = ehc, IntValue2 = ehm };
        }
        if (bestPotion != null) return new HitResult { Kind = HitKind.Potion, Potion = bestPotion };
        if (bestPower  != null) return new HitResult { Kind = HitKind.Power,  Power = bestPower  };
        if (bestRelic  != null) return new HitResult { Kind = HitKind.Relic,  Relic = bestRelic  };

        if (goldHit != null && TryReadGold(goldHit, out var gold))
            return new HitResult { Kind = HitKind.Gold, IntValue = gold };
        if (hpHit != null && TryReadHp(hpHit, out var cur, out var max, out var blk))
            return new HitResult { Kind = HitKind.Hp, IntValue = cur, IntValue2 = max, BlockValue = blk };
        if (energyHit != null && TryReadEnergy(energyHit, out var ce, out var me))
            return new HitResult { Kind = HitKind.Energy, IntValue = ce, IntValue2 = me };
        if (deckHit != null && TryReadDeckCount(deckHit, out var dc))
            return new HitResult { Kind = HitKind.Deck, IntValue = dc };
        if (drawHit != null && TryReadPileCount("DrawPile", out var dpc))
            return new HitResult { Kind = HitKind.DrawPile, IntValue = dpc };
        if (discardHit != null && TryReadPileCount("DiscardPile", out var dipc))
            return new HitResult { Kind = HitKind.DiscardPile, IntValue = dipc };
        if (exhaustHit != null && TryReadPileCount("ExhaustPile", out var epc))
            return new HitResult { Kind = HitKind.ExhaustPile, IntValue = epc };

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
                        if (d < bestEnemyDist) { bestEnemyDist = d; bestEnemyNode = nc; }
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
                else if (typeName == "NHealthBar" && ctrl.GetGlobalRect().HasPoint(mousePos))
                {
                    // Only fire on the LOCAL player's HP bar — NHealthBar
                    // exists for every creature (enemies + teammates), and we
                    // don't want to broadcast a teammate's HP as if it were
                    // ours. Match by creature reference.
                    if (IsLocalPlayerHealthBar(ctrl)) hpHit = ctrl;
                }
                else if (typeName == "NEnergyCounter" && ctrl.GetGlobalRect().HasPoint(mousePos)) energyHit = ctrl;
                else if (typeName == "NTopBarDeckButton" && ctrl.GetGlobalRect().HasPoint(mousePos)) deckHit = ctrl;
                else if (typeName == "NDrawPileButton" && ctrl.GetGlobalRect().HasPoint(mousePos)) drawHit = ctrl;
                else if (typeName == "NDiscardPileButton" && ctrl.GetGlobalRect().HasPoint(mousePos)) discardHit = ctrl;
                else if (typeName == "NExhaustPileButton" && ctrl.GetGlobalRect().HasPoint(mousePos)) exhaustHit = ctrl;
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

    private static bool TryReadHp(Control hp, out int current, out int max, out int block)
    {
        current = 0; max = 0; block = 0;
        try
        {
            // NHealthBar has _creature directly. Read HP/Block off it.
            var creature = hp.GetType().GetField("_creature", PrivInst)?.GetValue(hp);
            if (creature == null) return false;
            var ct = creature.GetType();
            var cur = ct.GetProperty("CurrentHp")?.GetValue(creature);
            var mx  = ct.GetProperty("MaxHp")?.GetValue(creature);
            var bk  = ct.GetProperty("Block")?.GetValue(creature);
            if (cur is int ci && mx is int mi) { current = ci; max = mi; }
            else return false;
            if (bk is int bi) block = bi;
            return true;
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] hp read fail: {ex.Message}"); }
        return false;
    }

    private static bool IsLocalPlayerHealthBar(Control hb)
    {
        try
        {
            var creature = hb.GetType().GetField("_creature", PrivInst)?.GetValue(hb);
            if (creature == null) return false;
            // Quick reject: enemies have an in-combat health bar but we never
            // want to broadcast enemy HP as "我血量".
            var isEnemy = creature.GetType().GetProperty("IsEnemy")?.GetValue(creature);
            if (isEnemy is bool e && e) return false;

            // Match against local player's Creature.
            var rmType = Type.GetType("MegaCrit.Sts2.Core.Runs.RunManager, sts2");
            var rm = rmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var ns = rm?.GetType().GetProperty("NetService")?.GetValue(rm);
            if (ns?.GetType().GetProperty("NetId")?.GetValue(ns) is not ulong localNetId) return false;

            var stateProp = rmType!.GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateProp?.GetValue(rm);
            if (state?.GetType().GetProperty("Players")?.GetValue(state) is not System.Collections.IEnumerable players)
                return false;
            foreach (var p in players)
            {
                if (p == null) continue;
                if (p.GetType().GetProperty("NetId")?.GetValue(p) is not ulong pid || pid != localNetId) continue;
                var localCreature = p.GetType().GetProperty("Creature")?.GetValue(p);
                return ReferenceEquals(localCreature, creature);
            }
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] IsLocalPlayerHealthBar: {ex.Message}"); }
        return false;
    }

    private static bool TryReadCreatureHp(NCreature nc, out int current, out int max)
    {
        current = 0; max = 0;
        try
        {
            var c = nc.Entity;
            if (c == null) return false;
            var ct = c.GetType();
            var cur = ct.GetProperty("CurrentHp")?.GetValue(c);
            var mx  = ct.GetProperty("MaxHp")?.GetValue(c);
            if (cur is int ci && mx is int mi) { current = ci; max = mi; return true; }
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] enemy hp read fail: {ex.Message}"); }
        return false;
    }

    private static bool TryReadDeckCount(Control deck, out int count)
    {
        count = 0;
        try
        {
            var pileField = deck.GetType().GetField("_pile", PrivInst);
            var pile = pileField?.GetValue(deck);
            // CardPile.Cards: IReadOnlyList<CardModel>
            var cardsProp = pile?.GetType().GetProperty("Cards");
            if (cardsProp?.GetValue(pile) is System.Collections.IEnumerable e)
            {
                foreach (var _ in e) count++;
                return true;
            }
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] deck count fail: {ex.Message}"); }
        return false;
    }

    /// <summary>Read a combat pile count by name from local player's PlayerCombatState.</summary>
    private static bool TryReadPileCount(string pileName, out int count)
    {
        count = 0;
        try
        {
            // Use existing resolver to find the local player.
            var rmType = Type.GetType("MegaCrit.Sts2.Core.Runs.RunManager, sts2");
            var rm = rmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var stateProp = rmType?.GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateProp?.GetValue(rm);
            var playersProp = state?.GetType().GetProperty("Players");
            if (playersProp?.GetValue(state) is not System.Collections.IEnumerable players) return false;

            // Find the local player by NetId (PlayerSlotResolver does this for combat;
            // here we just need *a* local player — single-player has only one, MP needs
            // matching. For simplicity in V1, take the first; pile counts are local-side
            // anyway and SP/host case dominates our use).
            var localId = VoiceRoulette.Net.PlayerSlotResolver.ResolveLocalPlayerId();
            object? player = null;
            foreach (var p in players)
            {
                if (localId is ulong me)
                {
                    var pNet = p.GetType().GetProperty("NetId")?.GetValue(p);
                    if (pNet is ulong pn && pn == me) { player = p; break; }
                }
                player ??= p;  // fallback: first player
            }
            if (player == null) return false;

            var pcsProp = player.GetType().GetProperty("PlayerCombatState");
            var pcs = pcsProp?.GetValue(player);
            var pile = pcs?.GetType().GetProperty(pileName)?.GetValue(pcs);
            var cardsProp = pile?.GetType().GetProperty("Cards");
            if (cardsProp?.GetValue(pile) is System.Collections.IEnumerable e)
            {
                foreach (var _ in e) count++;
                return true;
            }
        }
        catch (Exception ex) { GD.Print($"[VR][Pinger] {pileName} count fail: {ex.Message}"); }
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
