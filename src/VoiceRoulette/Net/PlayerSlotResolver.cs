using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;

namespace VoiceRoulette.Net;

/// <summary>
/// Reflective bridge to STS2's multiplayer model. Resolves the local player's
/// slot index, maps incoming bus senderIds to slot indices, and exposes the
/// per-slot creature anchor used for speech bubbles.
/// </summary>
/// <remarks>
/// Two ways to find the per-player state objects:
/// 1. Walk the Godot scene tree for nodes whose runtime type is (or extends)
///    NMultiplayerPlayerState. Catches scene-attached states like the UI rows.
/// 2. Find an NMultiplayerPlayerStateContainer in the tree, read its Players
///    property — the states may be plain objects held in a list, NOT scene
///    nodes. This is the more reliable source.
///
/// We try both and merge results, so we work no matter how the game wires
/// these together in the current build.
/// </remarks>
public static class PlayerSlotResolver
{
    private static SceneTree? _tree;
    private static Type? _stateBaseType;
    private static Type? _containerType;
    private static int _diagDumpsRemaining = 1;

    // Cached discovered property handles, keyed by the holder type.
    private static readonly Dictionary<Type, PropertyInfo?> _indexProp     = new();
    private static readonly Dictionary<Type, PropertyInfo?> _playerIdProp  = new();
    private static readonly Dictionary<Type, PropertyInfo?> _peerIdProp    = new();
    private static readonly Dictionary<Type, PropertyInfo?> _isLocalProp   = new();
    private static readonly Dictionary<Type, PropertyInfo?> _creatureProp  = new();
    private static readonly Dictionary<Type, PropertyInfo?> _playersProp   = new();

    private static SceneTree Tree => _tree ??= (SceneTree)Engine.GetMainLoop();

    private static Type? StateBaseType => _stateBaseType ??=
        Type.GetType("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState, sts2");

    private static Type? ContainerType => _containerType ??=
        Type.GetType("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerStateContainer, sts2");

    /// <summary>Local player's slot index (0-based). Returns 0 if unknown.</summary>
    public static byte ResolveLocalSlot()
    {
        foreach (var s in FindAllPlayerStates())
        {
            if (ReadBool(s, _isLocalProp, "IsLocal") == true)
                return ReadByteIndex(s) ?? 0;
        }
        return 0;
    }

    /// <summary>Local player's STS2 ulong PlayerId, or null if unknown.</summary>
    public static ulong? ResolveLocalPlayerId()
    {
        foreach (var s in FindAllPlayerStates())
        {
            if (ReadBool(s, _isLocalProp, "IsLocal") == true)
            {
                var pid = ReadUlong(s, _playerIdProp, "PlayerId")
                       ?? ReadUlong(s, _peerIdProp, "PeerId");
                if (pid is ulong u) return u;
            }
        }
        return null;
    }

    /// <summary>
    /// Maps a STS2 bus senderId (ulong from MessageHandlerDelegate) to a
    /// slot index. Tries PlayerId first, then PeerId. Returns null if no match.
    /// </summary>
    public static byte? ResolveSlotFromBusSenderId(ulong senderId)
    {
        var states = FindAllPlayerStates();
        foreach (var s in states)
        {
            if (ReadUlong(s, _playerIdProp, "PlayerId") == senderId)
                return ReadByteIndex(s);
        }
        foreach (var s in states)
        {
            if (ReadUlong(s, _peerIdProp, "PeerId") == senderId)
                return ReadByteIndex(s);
        }
        return null;
    }

    /// <summary>Position of the slot's character on the battlefield, or null.</summary>
    public static Vector2? TryGetCreaturePositionForSlot(byte slot)
    {
        foreach (var s in FindAllPlayerStates())
        {
            if (ReadByteIndex(s) != slot) continue;
            var creature = ReadObject(s, _creatureProp, "Creature");
            if (creature is Node2D n2d && n2d.IsVisibleInTree())
                return n2d.GlobalPosition;
            if (creature is Control ctrl && ctrl.IsVisibleInTree())
                return ctrl.GlobalPosition + ctrl.Size / 2f;
        }
        return null;
    }

    /// <summary>Position of the slot's UI portrait row (HUD), or null.</summary>
    public static Vector2? TryGetPortraitPositionForSlot(byte slot)
    {
        foreach (var s in FindAllPlayerStates())
        {
            if (ReadByteIndex(s) != slot) continue;
            if (s is Node2D n2d) return n2d.GlobalPosition;
            if (s is Control ctrl) return ctrl.GlobalPosition + new Vector2(ctrl.Size.X + 8, 14);
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Discovery — tries scene tree first, then container.Players
    // -------------------------------------------------------------------------

    private static List<object> FindAllPlayerStates()
    {
        var result = new List<object>();
        var seen = new HashSet<object>();
        if (Tree?.Root == null) return result;

        // 1. Scene tree walk — finds nodes whose runtime type is or extends
        //    NMultiplayerPlayerState (catches NMultiplayerPlayerExpandedState).
        var baseType = StateBaseType;
        WalkScene(Tree.Root, baseType, result, seen);

        // 2. Container.Players walk — finds plain (non-Node) state objects
        //    held in the multiplayer state container's player list.
        var container = FindContainer(Tree.Root);
        if (container != null)
        {
            var players = ReadObject(container, _playersProp, "Players");
            if (players is IEnumerable enumerable)
            {
                foreach (var p in enumerable)
                {
                    if (p != null && seen.Add(p)) result.Add(p);
                }
            }
        }

        if (_diagDumpsRemaining > 0 && result.Count > 0)
        {
            _diagDumpsRemaining--;
            DumpDiagnostics(result);
        }
        else if (_diagDumpsRemaining > 0 && result.Count == 0)
        {
            // Don't burn the budget; only dump when we actually found something.
        }

        return result;
    }

    private static void WalkScene(Node n, Type? baseType, List<object> bag, HashSet<object> seen)
    {
        if (baseType != null)
        {
            if (baseType.IsInstanceOfType(n) && seen.Add(n)) bag.Add(n);
        }
        else
        {
            // Fallback to name match if type couldn't be loaded.
            var tn = n.GetType().Name;
            if ((tn == "NMultiplayerPlayerState" || tn == "NMultiplayerPlayerExpandedState") && seen.Add(n))
                bag.Add(n);
        }
        var children = n.GetChildren();
        for (int i = 0; i < children.Count; i++) WalkScene(children[i], baseType, bag, seen);
    }

    private static Node? FindContainer(Node n)
    {
        var ct = ContainerType;
        if (ct != null && ct.IsInstanceOfType(n)) return n;
        if (ct == null && n.GetType().Name == "NMultiplayerPlayerStateContainer") return n;

        var children = n.GetChildren();
        for (int i = 0; i < children.Count; i++)
        {
            var found = FindContainer(children[i]);
            if (found != null) return found;
        }
        return null;
    }

    private static void DumpDiagnostics(List<object> states)
    {
        GD.Print($"[VR][Resolver] discovered {states.Count} player state(s):");
        for (int i = 0; i < states.Count; i++)
        {
            var s = states[i];
            var t = s.GetType();
            var idx     = ReadByteIndex(s);
            var pid     = ReadUlong(s, _playerIdProp, "PlayerId");
            var peerId  = ReadUlong(s, _peerIdProp, "PeerId");
            var local   = ReadBool(s, _isLocalProp, "IsLocal");
            var node    = s is Node ? "Node" : "Object";
            GD.Print($"[VR][Resolver]   [{i}] type={t.Name} kind={node} Index={(idx?.ToString() ?? "?")} PlayerId={(pid?.ToString() ?? "?")} PeerId={(peerId?.ToString() ?? "?")} IsLocal={(local?.ToString() ?? "?")}");
        }
    }

    // -------------------------------------------------------------------------
    // Reflection helpers (cached per type, work on Node OR plain object)
    // -------------------------------------------------------------------------

    private static byte? ReadByteIndex(object o)
    {
        var prop = GetCachedProp(o.GetType(), _indexProp, "Index");
        if (prop == null) return null;
        try
        {
            var v = prop.GetValue(o);
            return v switch
            {
                byte b   => b,
                int i    => (byte)i,
                short sh => (byte)sh,
                long l   => (byte)l,
                _        => null,
            };
        }
        catch { return null; }
    }

    private static bool? ReadBool(object o, Dictionary<Type, PropertyInfo?> cache, string name)
    {
        var prop = GetCachedProp(o.GetType(), cache, name);
        if (prop == null) return null;
        try { return prop.GetValue(o) as bool?; }
        catch { return null; }
    }

    private static ulong? ReadUlong(object o, Dictionary<Type, PropertyInfo?> cache, string name)
    {
        var prop = GetCachedProp(o.GetType(), cache, name);
        if (prop == null) return null;
        try
        {
            return prop.GetValue(o) switch
            {
                ulong u => u,
                long l  => (ulong)l,
                int i   => (ulong)i,
                uint ui => ui,
                _       => null,
            };
        }
        catch { return null; }
    }

    private static object? ReadObject(object o, Dictionary<Type, PropertyInfo?> cache, string name)
    {
        var prop = GetCachedProp(o.GetType(), cache, name);
        if (prop == null) return null;
        try { return prop.GetValue(o); }
        catch { return null; }
    }

    private static PropertyInfo? GetCachedProp(Type t, Dictionary<Type, PropertyInfo?> cache, string name)
    {
        if (cache.TryGetValue(t, out var cached)) return cached;
        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        cache[t] = prop;
        return prop;
    }
}
