using System;
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
/// Why reflection:
/// - <c>NMultiplayerPlayerState</c> and <c>NMultiplayerPlayerStateContainer</c>
///   are part of the game's runtime types but their property shapes can shift
///   between game versions. Reflection lets us tolerate field renames or new
///   wrappers without recompiling.
/// - We cache discovered <see cref="PropertyInfo"/> handles per type so the
///   per-frame cost is just a dictionary lookup + method call.
///
/// Discovered shape (from sts2.dll string table, 2026-05-07):
///   NMultiplayerPlayerStateContainer: Players (IList), MaxPlayers, NumPlayers
///   NMultiplayerPlayerState: Index (byte/int), PlayerId (ulong), PeerId,
///                            IsLocal (bool), Creature (NCreature), MultiplayerPlayerContainer
///   NCreature: GlobalPosition (Vector2 via Node2D)
/// </remarks>
public static class PlayerSlotResolver
{
    private const byte InvalidSlot = byte.MaxValue;

    private static SceneTree? _tree;
    // Cached discovered property handles, keyed by PropertyInfo's DeclaringType.
    private static readonly Dictionary<Type, PropertyInfo?> _indexProp     = new();
    private static readonly Dictionary<Type, PropertyInfo?> _playerIdProp  = new();
    private static readonly Dictionary<Type, PropertyInfo?> _isLocalProp   = new();
    private static readonly Dictionary<Type, PropertyInfo?> _creatureProp  = new();

    private static SceneTree Tree => _tree ??= (SceneTree)Engine.GetMainLoop();

    /// <summary>
    /// Returns the local player's slot index (0-based). Returns 0 if no
    /// multiplayer session is active or the lookup fails — that's the
    /// safe default for singleplayer / pre-game.
    /// </summary>
    public static byte ResolveLocalSlot()
    {
        var states = FindAllPlayerStates();
        foreach (var s in states)
        {
            var isLocal = ReadBool(s, _isLocalProp, "IsLocal");
            if (isLocal == true)
                return ReadByteIndex(s) ?? 0;
        }
        return 0;
    }

    /// <summary>
    /// Maps a STS2 bus senderId (ulong from MessageHandlerDelegate) to a
    /// slot index (0-3). Returns null if no match — caller should treat as
    /// "unknown peer" and pick a fallback.
    /// </summary>
    public static byte? ResolveSlotFromBusSenderId(ulong senderId)
    {
        var states = FindAllPlayerStates();
        foreach (var s in states)
        {
            var pid = ReadUlong(s, _playerIdProp, "PlayerId");
            if (pid == senderId) return ReadByteIndex(s);
        }
        // Some game builds use PeerId instead of PlayerId on the bus side.
        // Try a fallback search in case the names diverge.
        foreach (var s in states)
        {
            var pid = ReadUlong(s, null, "PeerId");
            if (pid == senderId) return ReadByteIndex(s);
        }
        return null;
    }

    /// <summary>
    /// Returns the on-screen position of the creature owned by the given slot
    /// (player's character), or null if no such creature exists in the current
    /// scene (e.g. outside combat).
    /// </summary>
    public static Vector2? TryGetCreaturePositionForSlot(byte slot)
    {
        var states = FindAllPlayerStates();
        foreach (var s in states)
        {
            var idx = ReadByteIndex(s);
            if (idx != slot) continue;
            var creature = ReadObject(s, _creatureProp, "Creature");
            if (creature is Node2D n2d && n2d.IsVisibleInTree())
                return n2d.GlobalPosition;
            if (creature is Control ctrl && ctrl.IsVisibleInTree())
                return ctrl.GlobalPosition + ctrl.Size / 2f;
        }
        return null;
    }

    /// <summary>
    /// Returns the player UI portrait position for the given slot (the row
    /// in the top-left HUD). Used as fallback when the creature isn't on
    /// screen — e.g. on the map, in shop, etc.
    /// </summary>
    public static Vector2? TryGetPortraitPositionForSlot(byte slot)
    {
        var states = FindAllPlayerStates();
        foreach (var s in states)
        {
            var idx = ReadByteIndex(s);
            if (idx != slot) continue;
            if (s is Node2D n2d) return n2d.GlobalPosition;
            if (s is Control ctrl) return ctrl.GlobalPosition + new Vector2(ctrl.Size.X + 8, 14);
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
        WalkForType(Tree.Root, "NMultiplayerPlayerState", result);
        return result;
    }

    private static void WalkForType(Node n, string typeName, List<Node> bag)
    {
        if (n.GetType().Name == typeName) bag.Add(n);
        var children = n.GetChildren();
        for (int i = 0; i < children.Count; i++) WalkForType(children[i], typeName, bag);
    }

    // -------------------------------------------------------------------------
    // Reflection helpers (cached per type)
    // -------------------------------------------------------------------------

    private static byte? ReadByteIndex(Node n)
    {
        var prop = GetCachedProp(n.GetType(), _indexProp, "Index");
        if (prop == null) return null;
        try
        {
            var v = prop.GetValue(n);
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

    private static bool? ReadBool(Node n, Dictionary<Type, PropertyInfo?> cache, string name)
    {
        var prop = GetCachedProp(n.GetType(), cache, name);
        if (prop == null) return null;
        try { return prop.GetValue(n) as bool?; }
        catch { return null; }
    }

    private static ulong? ReadUlong(Node n, Dictionary<Type, PropertyInfo?>? cache, string name)
    {
        var prop = cache != null
            ? GetCachedProp(n.GetType(), cache, name)
            : n.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return null;
        try
        {
            return prop.GetValue(n) switch
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

    private static object? ReadObject(Node n, Dictionary<Type, PropertyInfo?> cache, string name)
    {
        var prop = GetCachedProp(n.GetType(), cache, name);
        if (prop == null) return null;
        try { return prop.GetValue(n); }
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
