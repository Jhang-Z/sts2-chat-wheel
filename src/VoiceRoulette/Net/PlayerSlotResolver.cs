using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace VoiceRoulette.Net;

/// <summary>
/// Maps STS2 multiplayer peers to local "slot" indices used by the wheel
/// for audio/bubble routing. Local player is always slot 0; each remote
/// senderId is assigned a stable slot ≥ 1 on first sight.
///
/// Why this shape: AudioPlayer has per-slot AudioStreamPlayer instances —
/// if two distinct peers route to the SAME slot, their voice streams
/// overwrite each other. Keeping locals at 0 and remotes at 1+ guarantees
/// no overlap regardless of NetId entropy.
///
/// Player data discovery (from sts2.dll IL inspection):
///   NMultiplayerPlayerState (UI widget) → has `Player` property
///   Player → has `NetId: UInt64`, `Creature: Creature`, etc.
///   INetGameService → has `NetId` for the local peer
/// </summary>
public static class PlayerSlotResolver
{
    private static SceneTree? _tree;
    private static Type? _stateBaseType;
    private static int _diagDumpsRemaining = 1;

    private static readonly Dictionary<ulong, byte> _remoteSlotMap = new();
    private static byte _nextRemoteSlot = 1;

    private static SceneTree Tree => _tree ??= (SceneTree)Engine.GetMainLoop();

    private static Type? StateBaseType => _stateBaseType ??=
        Type.GetType("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState, sts2");

    /// <summary>
    /// Reset the remote-slot assignments. Call when a co-op session ends
    /// so a future session starts with a clean slate.
    /// </summary>
    public static void Reset()
    {
        _remoteSlotMap.Clear();
        _nextRemoteSlot = 1;
    }

    /// <summary>Local player slot. Always 0 from the local peer's POV.</summary>
    public static byte ResolveLocalSlot() => 0;

    /// <summary>Local player's STS2 NetId, or null if no co-op session.</summary>
    public static ulong? ResolveLocalPlayerId() => RunManager.Instance?.NetService?.NetId;

    /// <summary>
    /// Map a bus senderId to a slot. Local senderId → 0. Each remote senderId
    /// gets a stable assignment of 1, 2, 3, ... on first sight.
    /// </summary>
    public static byte? ResolveSlotFromBusSenderId(ulong senderId)
    {
        var localId = ResolveLocalPlayerId();
        if (localId is ulong me && senderId == me) return 0;

        if (_remoteSlotMap.TryGetValue(senderId, out var existing)) return existing;
        var slot = _nextRemoteSlot++;
        _remoteSlotMap[senderId] = slot;
        GD.Print($"[VR][Resolver] assigned remote senderId={senderId} → slot={slot}");
        return slot;
    }

    /// <summary>
    /// Find the on-screen Creature position for the player at this slot.
    /// Returns null if no matching player state is in the scene tree.
    /// </summary>
    public static Vector2? TryGetCreaturePositionForSlot(byte slot)
    {
        var netId = NetIdForSlot(slot);
        if (netId is null) return null;
        return GetCreaturePositionForNetId(netId.Value);
    }

    public static Vector2? TryGetPortraitPositionForSlot(byte slot)
    {
        var netId = NetIdForSlot(slot);
        if (netId is null) return null;
        return GetPortraitPositionForNetId(netId.Value);
    }

    // -------------------------------------------------------------------------
    // Internal lookups
    // -------------------------------------------------------------------------

    private static ulong? NetIdForSlot(byte slot)
    {
        if (slot == 0) return ResolveLocalPlayerId();
        foreach (var kv in _remoteSlotMap)
            if (kv.Value == slot) return kv.Key;
        return null;
    }

    private static Vector2? GetCreaturePositionForNetId(ulong netId)
    {
        foreach (var state in FindAllPlayerStates())
        {
            var player = ReadProp(state, "Player");
            if (player == null) continue;
            var pNetId = ReadUlong(player, "NetId");
            if (pNetId != netId) continue;
            var creature = ReadProp(player, "Creature");
            if (creature is Node2D n2d && n2d.IsVisibleInTree())
                return n2d.GlobalPosition;
            if (creature is Control ctrl && ctrl.IsVisibleInTree())
                return ctrl.GlobalPosition + ctrl.Size / 2f;
        }
        return null;
    }

    private static Vector2? GetPortraitPositionForNetId(ulong netId)
    {
        foreach (var state in FindAllPlayerStates())
        {
            var player = ReadProp(state, "Player");
            if (player == null) continue;
            if (ReadUlong(player, "NetId") != netId) continue;
            if (state is Node2D n2d) return n2d.GlobalPosition;
            if (state is Control ctrl) return ctrl.GlobalPosition + new Vector2(ctrl.Size.X + 8, 14);
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Tree walk
    // -------------------------------------------------------------------------

    private static List<Node> FindAllPlayerStates()
    {
        var result = new List<Node>();
        if (Tree?.Root == null) return result;
        var baseType = StateBaseType;
        WalkScene(Tree.Root, baseType, result);

        if (_diagDumpsRemaining > 0 && result.Count > 0)
        {
            _diagDumpsRemaining--;
            DumpDiagnostics(result);
        }
        return result;
    }

    private static void WalkScene(Node n, Type? baseType, List<Node> bag)
    {
        if (baseType != null)
        {
            if (baseType.IsInstanceOfType(n)) bag.Add(n);
        }
        else
        {
            var tn = n.GetType().Name;
            if (tn == "NMultiplayerPlayerState" || tn == "NMultiplayerPlayerExpandedState")
                bag.Add(n);
        }
        var children = n.GetChildren();
        for (int i = 0; i < children.Count; i++) WalkScene(children[i], baseType, bag);
    }

    private static void DumpDiagnostics(List<Node> states)
    {
        var localId = ResolveLocalPlayerId();
        GD.Print($"[VR][Resolver] discovered {states.Count} player state(s); localNetId={(localId?.ToString() ?? "null")}");
        for (int i = 0; i < states.Count; i++)
        {
            var s = states[i];
            var player = ReadProp(s, "Player");
            var netId = player != null ? ReadUlong(player, "NetId") : null;
            var creature = player != null ? ReadProp(player, "Creature") : null;
            var creatureKind = creature switch
            {
                Node2D n2d => $"Node2D@{n2d.GlobalPosition}",
                Control c => $"Control@{c.GlobalPosition}",
                null => "null",
                _ => creature.GetType().Name,
            };
            var isLocal = (netId.HasValue && localId.HasValue && netId.Value == localId.Value) ? "✓" : "";
            GD.Print($"[VR][Resolver]   [{i}] state={s.GetType().Name} Player.NetId={(netId?.ToString() ?? "?")} {isLocal} Creature={creatureKind}");
        }
    }

    // -------------------------------------------------------------------------
    // Reflection helpers
    // -------------------------------------------------------------------------

    private static object? ReadProp(object o, string name)
    {
        var t = o.GetType();
        for (var ct = t; ct != null && ct != typeof(object); ct = ct.BaseType)
        {
            var prop = ct.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (prop != null)
            {
                try
                {
                    var v = prop.GetValue(o);
                    if (v != null) return v;
                }
                catch { }
            }
            var field = ct.GetField($"<{name}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                try { return field.GetValue(o); } catch { }
            }
        }
        return null;
    }

    private static ulong? ReadUlong(object o, string name)
    {
        return ReadProp(o, name) switch
        {
            ulong u => u,
            long l => (ulong)l,
            int i => (ulong)i,
            uint ui => ui,
            _ => null,
        };
    }
}
