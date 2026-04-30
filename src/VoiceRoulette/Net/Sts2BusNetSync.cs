// CONFIRMED API (from reflection on sts2.dll, 2026-05-01):
//
// MegaCrit.Sts2.Core.Multiplayer.Serialization.INetMessage  [public interface]
//   extends IPacketSerializable { Serialize(PacketWriter), Deserialize(PacketReader) }
//   bool ShouldBroadcast { get; }
//   NetTransferMode Mode { get; }           // enum: None=0, Unreliable=1, Reliable=2
//   MegaCrit.Sts2.Core.Logging.LogLevel LogLevel { get; }
//
// MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService  [public interface]
//   void RegisterMessageHandler<T>(MessageHandlerDelegate<T>)   where T : INetMessage
//   void UnregisterMessageHandler<T>(MessageHandlerDelegate<T>) where T : INetMessage
//   void SendMessage<T>(T message)                               where T : INetMessage
//
// MegaCrit.Sts2.Core.Multiplayer.Game.MessageHandlerDelegate<T>  [public delegate]
//   void Invoke(T message, ulong senderId)
//
// MegaCrit.Sts2.Core.Runs.RunManager  [public singleton]
//   static RunManager Instance { get; }
//   INetGameService NetService { get; }    // null in singleplayer; non-null in co-op
//
// MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes  [public class]
//   static NetTypeCache<INetMessage> _cache  [private static initonly]
//   NetTypeCache<INetMessage>._typeToId  Dictionary<Type,int>  [initonly but mutable contents]
//   NetTypeCache<INetMessage>._idToType  List<Type>            [initonly but mutable contents]
//
// INJECTION STRATEGY:
//   INetMessageSubtypes._subtypes is a hardcoded static array of 49 game types.
//   Custom message types are not in it. TryDeserializeMessage on the receiving peer
//   uses MessageTypes._cache to map a packet's type-ID prefix to a concrete Type.
//   Because _cache._typeToId and _cache._idToType are mutable reference types
//   (Dictionary and List — only the reference is initonly, not the contents) we can
//   add VoiceRouletteMessage at runtime. Both clients must call RegisterMessageType()
//   before any session message is exchanged — we call it in the constructor.
//   Consistent ID is guaranteed as long as no two mods race to inject at the same index.

using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;

namespace VoiceRoulette.Net;

/// <summary>
/// NetSync transport backed by STS2's typed message bus.
/// Requires an active <see cref="INetGameService"/> (i.e. co-op session).
/// </summary>
public sealed class Sts2BusNetSync : INetSync, IDisposable
{
    public event Action<WireMessage>? LineReceived;

    private readonly INetGameService _netService;
    private readonly MessageHandlerDelegate<VoiceRouletteMessage> _handler;
    private bool _disposed;

    public Sts2BusNetSync(INetGameService netService)
    {
        _netService = netService ?? throw new ArgumentNullException(nameof(netService));

        RegisterMessageType();

        // Capture delegate once to be able to unregister the same instance later.
        _handler = HandleMessage;
        _netService.RegisterMessageHandler(_handler);
    }

    /// <summary>
    /// Convenience factory — reads <see cref="RunManager.Instance.NetService"/>.
    /// Returns null if no multiplayer session is active.
    /// </summary>
    public static Sts2BusNetSync? TryCreate()
    {
        var netService = RunManager.Instance?.NetService;
        return netService is { IsConnected: true }
            ? new Sts2BusNetSync(netService)
            : null;
    }

    public void Broadcast(WireMessage msg)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _netService.SendMessage(new VoiceRouletteMessage(msg));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _netService.UnregisterMessageHandler(_handler);
    }

    private void HandleMessage(VoiceRouletteMessage msg, ulong senderId)
    {
        var wire = msg.ToWireMessage();
        if (wire is not null)
            LineReceived?.Invoke(wire);
    }

    // -------------------------------------------------------------------------
    // Reflective type injection — isolated to this one method.
    // -------------------------------------------------------------------------

    private static bool _typeRegistered;

    /// <summary>
    /// Injects <see cref="VoiceRouletteMessage"/> into MessageTypes._cache so the
    /// game's NetMessageBus can serialize/deserialize our packet by type ID.
    /// Safe to call multiple times; only the first call mutates the cache.
    /// </summary>
    private static void RegisterMessageType()
    {
        if (_typeRegistered) return;
        _typeRegistered = true;

        InjectIntoMessageTypesCache(typeof(VoiceRouletteMessage));
    }

    private static void InjectIntoMessageTypesCache(Type messageType)
    {
        const string msgTypesName = "MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes";
        const string cacheFieldName = "_cache";
        const string typeToIdFieldName = "_typeToId";
        const string idToTypeFieldName = "_idToType";

        var msgTypesType = Type.GetType(msgTypesName + ", sts2")
            ?? throw new InvalidOperationException($"Cannot find {msgTypesName}");

        var cacheField = msgTypesType.GetField(cacheFieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Cannot find {msgTypesName}.{cacheFieldName}");

        var cache = cacheField.GetValue(null)
            ?? throw new InvalidOperationException("MessageTypes._cache is null");

        var cacheType = cache.GetType();

        var typeToIdField = cacheType.GetField(typeToIdFieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Cannot find NetTypeCache.{typeToIdFieldName}");
        var idToTypeField = cacheType.GetField(idToTypeFieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Cannot find NetTypeCache.{idToTypeFieldName}");

        var typeToId = (Dictionary<Type, int>)typeToIdField.GetValue(cache)!;
        var idToType = (List<Type>)idToTypeField.GetValue(cache)!;

        // Guard: only inject if not already present (idempotent across hot-reloads).
        if (typeToId.ContainsKey(messageType)) return;

        var newId = idToType.Count;
        idToType.Add(messageType);
        typeToId[messageType] = newId;
    }
}
