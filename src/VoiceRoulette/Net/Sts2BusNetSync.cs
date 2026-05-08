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
using System.Linq;
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
    public event Action<MarkerWire>? MarkerReceived;

    private readonly INetGameService _netService;
    private readonly MessageHandlerDelegate<VoiceRouletteMessage> _voiceHandler;
    private readonly MessageHandlerDelegate<TargetMarkerMessage> _markerHandler;
    private bool _disposed;

    public Sts2BusNetSync(INetGameService netService)
    {
        _netService = netService ?? throw new ArgumentNullException(nameof(netService));

        // Inject both message types at fixed IDs so peers agree on the wire
        // prefix regardless of game state at injection time.
        InjectIntoMessageTypesCache(typeof(VoiceRouletteMessage), VoiceFixedTypeId);
        InjectIntoMessageTypesCache(typeof(TargetMarkerMessage),  MarkerFixedTypeId);
        VerifyInjection(typeof(VoiceRouletteMessage), VoiceFixedTypeId);
        VerifyInjection(typeof(TargetMarkerMessage),  MarkerFixedTypeId);

        _voiceHandler  = HandleVoiceMessage;
        _markerHandler = HandleMarkerMessage;
        _netService.RegisterMessageHandler(_voiceHandler);
        _netService.RegisterMessageHandler(_markerHandler);
        Godot.GD.Print($"[VR][Net] Sts2BusNetSync ready — handlers registered for VoiceRouletteMessage + TargetMarkerMessage");
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
        Godot.GD.Print($"[VR][Net] bus broadcast: sender={msg.Sender} text='{msg.Text}' emotion={msg.Emotion ?? "null"}");
        _netService.SendMessage(new VoiceRouletteMessage(msg));
    }

    public void BroadcastMarker(MarkerWire marker)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Godot.GD.Print($"[VR][Net] bus broadcast marker: sender={marker.Sender} pos=({marker.X}, {marker.Y})");
        _netService.SendMessage(new TargetMarkerMessage(marker));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _netService.UnregisterMessageHandler(_voiceHandler);
        _netService.UnregisterMessageHandler(_markerHandler);
    }

    private void HandleVoiceMessage(VoiceRouletteMessage msg, ulong senderId)
    {
        Godot.GD.Print($"[VR][Net] bus received voice: senderId={senderId}");
        var wire = msg.ToWireMessage();
        if (wire is null)
        {
            Godot.GD.PrintErr("[VR][Net] failed to deserialize wire from bus message");
            return;
        }

        var localId = PlayerSlotResolver.ResolveLocalPlayerId();
        if (localId is ulong me && me == senderId)
        {
            Godot.GD.Print($"[VR][Net] skipping self-echo (senderId={senderId} == localPlayerId)");
            return;
        }

        var resolved = PlayerSlotResolver.ResolveSlotFromBusSenderId(senderId);
        if (resolved is byte slot)
        {
            wire = wire with { Sender = slot };
        }
        else
        {
            var fakeSlot = (byte)(senderId & 0xFF);
            if (localId is ulong me2)
            {
                var localFake = (byte)(me2 & 0xFF);
                if (fakeSlot == localFake) fakeSlot = (byte)(fakeSlot + 1);
            }
            wire = wire with { Sender = fakeSlot };
        }

        LineReceived?.Invoke(wire);
    }

    private void HandleMarkerMessage(TargetMarkerMessage msg, ulong senderId)
    {
        Godot.GD.Print($"[VR][Net] bus received marker: senderId={senderId}");
        var marker = msg.ToMarkerWire();
        if (marker is null)
        {
            Godot.GD.PrintErr("[VR][Net] failed to deserialize marker");
            return;
        }

        // Self-echo filter — same as voice path.
        var localId = PlayerSlotResolver.ResolveLocalPlayerId();
        if (localId is ulong me && me == senderId) return;

        var resolved = PlayerSlotResolver.ResolveSlotFromBusSenderId(senderId);
        var slot = resolved ?? (byte)(senderId & 0xFF);
        marker = marker with { Sender = slot };

        MarkerReceived?.Invoke(marker);
    }

    // -------------------------------------------------------------------------
    // Diagnostic helpers — verify injection worked and act as a control test
    // -------------------------------------------------------------------------

    /// <summary>
    /// After injection, round-trip MessageTypes to verify our type is in the
    /// cache. If the round-trip fails, sending will silently no-op.
    /// </summary>
    private static void VerifyInjection(Type messageType, int expectedId)
    {
        try
        {
            var msgTypesType = Type.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes, sts2");
            if (msgTypesType == null) { Godot.GD.PrintErr("[VR][Net] VerifyInjection: MessageTypes type not loadable"); return; }

            // CRITICAL: BindingFlags.Static is required — these methods are static
            // and the default GetMethod overload only finds Public+Instance.
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var typeToIdMethod = msgTypesType.GetMethod("TypeToId", Flags, null, new[] { typeof(Type) }, null);
            var tryGetMethod   = msgTypesType.GetMethod("TryGetMessageType", Flags);
            if (typeToIdMethod == null || tryGetMethod == null)
            {
                Godot.GD.PrintErr($"[VR][Net] VerifyInjection: methods not found (typeToId={typeToIdMethod != null}, tryGet={tryGetMethod != null})");
                return;
            }
            var ourId = (int)typeToIdMethod.Invoke(null, new object[] { messageType })!;
            var args = new object?[] { ourId, null };
            var ok = (bool)tryGetMethod.Invoke(null, args)!;
            var roundTrip = args[1] as Type;
            Godot.GD.Print($"[VR][Net] VerifyInjection({messageType.Name}): id={ourId} reverse-ok={ok} reverse-type={roundTrip?.Name ?? "null"} (expected {expectedId})");
            if (!ok || roundTrip != messageType || ourId != expectedId)
                Godot.GD.PrintErr($"[VR][Net] VerifyInjection FAILED for {messageType.Name} — peers may not decode this packet");
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[VR][Net] VerifyInjection threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Reflective type injection — isolated to this one method.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Injects <see cref="VoiceRouletteMessage"/> into MessageTypes._cache so the
    /// game's NetMessageBus can serialize/deserialize our packet by type ID.
    /// The inject is itself idempotent — if the type is already at the fixed
    /// slot, it's a no-op; if at a different slot, it's moved.
    ///
    /// We deliberately do NOT use a static guard here. The previous version
    /// did, and it caused the force-to-fixed-id fix to never run on second
    /// session in the same process (mod hot-reload kept the static true while
    /// the cache had been populated by an earlier code path).
    /// </summary>
    /// <summary>
    /// Hardcoded IDs — MUST match across peers, otherwise sender's serialized
    /// prefix won't match receiver's lookup and packets get dropped silently.
    /// IDs are picked well above any built-in count (game's _t0.._t49 suggest
    /// ≤50 built-in types).
    /// </summary>
    private const int VoiceFixedTypeId  = 200;
    private const int MarkerFixedTypeId = 201;

    private static void InjectIntoMessageTypesCache(Type messageType, int fixedId)
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

        Godot.GD.Print($"[VR][Net] inject START({messageType.Name} → {fixedId}): pre typeToId.Count={typeToId.Count}, idToType.Count={idToType.Count}");

        // Force-move: if already registered at any other id, free that slot
        // first — we don't early-return because pre-existing dynamic IDs
        // diverge between peers.
        if (typeToId.TryGetValue(messageType, out var existingId))
        {
            Godot.GD.Print($"[VR][Net] inject: {messageType.Name} already at id={existingId}, force-moving to fixed id={fixedId}");
            typeToId.Remove(messageType);
            if (existingId >= 0 && existingId < idToType.Count && idToType[existingId] == messageType)
                idToType[existingId] = typeof(object);
        }

        while (idToType.Count < fixedId)
            idToType.Add(typeof(object));

        if (idToType.Count == fixedId)
            idToType.Add(messageType);
        else
            idToType[fixedId] = messageType;

        typeToId[messageType] = fixedId;

        Godot.GD.Print($"[VR][Net] inject DONE: {messageType.Name} at id={fixedId}, idToType.Count={idToType.Count}");
    }
}
