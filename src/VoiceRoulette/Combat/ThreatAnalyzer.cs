using Godot;
using System;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using VoiceRoulette.Net;

namespace VoiceRoulette.Combat;

/// <summary>
/// At the start of each player turn, scans enemy intents vs the local
/// player's HP+Block. If a single enemy attack would one-shot us, OR the
/// total expected enemy damage exceeds our buffer, broadcast a warning
/// voice line so teammates know to help.
///
/// Fires at most once per player turn — multiple "I'm in danger" pings
/// in 5 seconds would be noise.
/// </summary>
public sealed partial class ThreatAnalyzer : Node
{
    private SceneTree? _tree;
    private Action<string>? _onPing;
    private bool _warnedThisTurn;

    public void Start(Action<string> onPing)
    {
        _onPing = onPing;
        _tree = (SceneTree)Engine.GetMainLoop();
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        GD.Print("[VR][ThreatAnalyzer] Started — watching for lethal enemy intents");
    }

    public override void _ExitTree()
    {
        if (CombatManager.Instance != null)
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
    }

    private void OnTurnStarted(CombatState state)
    {
        if (state.CurrentSide != CombatSide.Player) return;
        _warnedThisTurn = false;

        // Wait for intents to settle (game animates them in).
        var timer = _tree!.CreateTimer(0.8);
        timer.Timeout += () => CheckThreat(state);
    }

    private void CheckThreat(CombatState state)
    {
        if (_warnedThisTurn || _onPing == null) return;

        var localPlayer = FindLocalPlayer(state);
        if (localPlayer?.Creature == null) return;

        var creature = localPlayer.Creature;
        if (!creature.IsAlive) return;
        var hpAndBlock = creature.CurrentHp + creature.Block;

        var (maxSingleHit, totalDamage) = ScanIncomingDamage(state);

        // Two danger heuristics:
        //   • Any one attack on its own ≥ our buffer → near-certain death (unblockable boss hit).
        //   • Sum of all attacks > our buffer → likely to die if all attacks target us.
        // The single-hit case is the one we really care about (false-alarms low);
        // the total-damage case is a softer warning.
        var oneShotKill = maxSingleHit >= hpAndBlock;
        var totalLethal = totalDamage > hpAndBlock;

        if (!oneShotKill && !totalLethal) return;

        _warnedThisTurn = true;
        var msg = oneShotKill ? "我快被秒了, 救我!" : "我血不够了, 顶不住了";
        GD.Print($"[VR][ThreatAnalyzer] WARN: HP+block={hpAndBlock} maxSingle={maxSingleHit} total={totalDamage} → {msg}");
        _onPing(msg);
    }

    private static (int maxSingleHit, int totalDamage) ScanIncomingDamage(CombatState state)
    {
        var maxSingle = 0;
        var total = 0;
        foreach (var enemy in state.Enemies)
        {
            if (enemy?.IsAlive != true) continue;
            var monster = enemy.Monster;
            var move = monster?.NextMove;
            if (move?.Intents == null) continue;

            foreach (var intent in move.Intents)
            {
                if (intent is not AttackIntent atk) continue;
                int dmg = 0;
                try { dmg = (int)(atk.DamageCalc?.Invoke() ?? 0); }
                catch { continue; }  // some intents need combat state we can't replicate; skip

                if (dmg <= 0) continue;
                var hits = Math.Max(1, atk.Repeats);
                var perIntent = dmg * hits;
                total += perIntent;

                // For "one-shot" purposes, treat each repeat as a separate hit
                // (Block resets between hits in STS but not between repeats of
                // the same intent — adjust if game version differs). Use the
                // single-attack damage as the unit.
                if (dmg > maxSingle) maxSingle = dmg;
            }
        }
        return (maxSingle, total);
    }

    private static Player? FindLocalPlayer(CombatState state)
    {
        var localId = PlayerSlotResolver.ResolveLocalPlayerId();
        if (localId is ulong me)
        {
            foreach (var p in state.Players)
                if (p.NetId == me) return p;
        }
        return state.Players.Count == 1 ? state.Players[0] : null;
    }
}
