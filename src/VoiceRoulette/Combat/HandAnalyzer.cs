using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using VoiceRoulette.Net;

namespace VoiceRoulette.Combat;

// At the start of each player turn, scans the hand and broadcasts a ping if
// any card can apply Vulnerable (易伤) or Weak (虚弱) to enemies.
public sealed partial class HandAnalyzer : Node
{
    private SceneTree? _tree;
    private Action<string>? _onPing;

    // Keywords match cards that APPLY the debuff, not cards that benefit from it.
    // "层易伤" matches "给予2层易伤" but NOT "如果该敌人有易伤状态".
    private static readonly string[] VulnerableKeywords = ["层易伤", "给予易伤", "Apply Vulnerable"];
    private static readonly string[] WeakKeywords       = ["层虚弱", "给予虚弱", "Apply Weak"];

    // Strip Godot RichText BBCode tags like [color=...], [gold], [/gold], [img]...[/img].
    // Card descriptions wrap keywords in tags, e.g. "给予2层[gold]易伤[/gold]" — without
    // stripping, "层易伤" wouldn't match.
    private static readonly Regex BBCodeRegex = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    public void Start(byte localSlot, Action<string> onPing)
    {
        // localSlot kept in signature for compatibility; we now resolve the
        // local player by NetId match because state.Players[0] is the host's
        // player on every peer — using it on a client scans the HOST's hand
        // and produces phantom "I have Vulnerable" pings on the wrong peer.
        _ = localSlot;
        _onPing = onPing;
        _tree = (SceneTree)Engine.GetMainLoop();

        CombatManager.Instance.TurnStarted += OnTurnStarted;
        GD.Print("[VR][HandAnalyzer] Started — watching for Vulnerable/Weak cards on turn start");
    }

    public override void _ExitTree()
    {
        if (CombatManager.Instance != null)
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
    }

    private void OnTurnStarted(CombatState state)
    {
        GD.Print($"[VR][HandAnalyzer] TurnStarted fired. side={state.CurrentSide} players={state.Players.Count}");
        // Only fire on the player's side, not the enemy turn.
        if (state.CurrentSide != CombatSide.Player) return;
        if (state.Players.Count == 0) return;

        // Short delay to let the draw queue populate Hand.Cards.
        // The visual draw animation is much longer but irrelevant — we read data, not UI.
        var timer = _tree!.CreateTimer(0.7);
        timer.Timeout += () => CheckHand(state);
    }

    private static Player? FindLocalPlayer(CombatState state)
    {
        var localId = PlayerSlotResolver.ResolveLocalPlayerId();
        if (localId is ulong me)
        {
            foreach (var p in state.Players)
                if (p.NetId == me) return p;
        }
        // Singleplayer / no co-op: there's only one player and it's us.
        return state.Players.Count == 1 ? state.Players[0] : null;
    }

    private void CheckHand(CombatState state)
    {
        if (_onPing == null) return;

        var player = FindLocalPlayer(state);
        if (player == null)
        {
            GD.Print("[VR][HandAnalyzer] CheckHand: could not identify local player by NetId — skipping");
            return;
        }
        var creature = player.Creature;
        var hand = player.PlayerCombatState?.Hand;
        if (hand == null)
        {
            GD.Print("[VR][HandAnalyzer] CheckHand: hand is null");
            return;
        }
        if (hand.IsEmpty)
        {
            GD.Print("[VR][HandAnalyzer] CheckHand: hand is empty");
            return;
        }

        GD.Print($"[VR][HandAnalyzer] CheckHand: scanning {hand.Cards.Count} cards");

        var hasVulnerable = false;
        var hasWeak = false;
        foreach (var card in hand.Cards)
        {
            string desc;
            try { desc = card.GetDescriptionForPile(PileType.Hand, creature) ?? ""; }
            catch (Exception ex) { GD.Print($"[VR][HandAnalyzer] desc error: {ex.Message}"); desc = ""; }

            // Strip BBCode tags so "给予2层[gold]易伤[/gold]" becomes "给予2层易伤".
            var plain = BBCodeRegex.Replace(desc, "");

            if (!hasVulnerable && ContainsAny(plain, VulnerableKeywords)) hasVulnerable = true;
            if (!hasWeak       && ContainsAny(plain, WeakKeywords))       hasWeak = true;

            if (hasVulnerable && hasWeak) break;
        }

        string? msg = (hasVulnerable, hasWeak) switch
        {
            (true,  true)  => "我有【易伤】和【虚弱】牌",
            (true,  false) => "我有【易伤】牌",
            (false, true)  => "我有【虚弱】牌",
            _              => null,
        };

        if (msg != null)
        {
            GD.Print($"[VR][HandAnalyzer] {msg}");
            _onPing(msg);
        }
    }

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
