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

    private readonly INetGameService _netService;
    private readonly MessageHandlerDelegate<VoiceRouletteMessage> _handler;
    private bool _disposed;

    public Sts2BusNetSync(INetGameService netService)
    {
        _netService = netService ?? throw new ArgumentNullException(nameof(netService));

        RegisterMessageType();
        VerifyInjection();

        // Capture delegate once to be able to unregister the same instance later.
        _handler = HandleMessage;
        _netService.RegisterMessageHandler(_handler);
        Godot.GD.Print($"[VR][Net] Sts2BusNetSync ready — handler registered for {typeof(VoiceRouletteMessage).Name}");

        TrySelfLoopbackTest();
    }

    /// <summary>
    /// Diagnostic: try the SendMessage(T, ulong) overload with our own NetId
    /// to see if the bus loops back to self. If our HandleMessage fires with
    /// a known marker text, we know the entire send→receive pipeline works
    /// for our type, and the issue with teammates not receiving must be in
    /// the network/peer-discovery layer.
    /// </summary>
    private void TrySelfLoopbackTest()
    {
        try
        {
            var myId = _netService.NetId;
            Godot.GD.Print($"[VR][Net] self-loopback test: sending to NetId={myId}");
            var probe = new WireMessage(WireMessage.CurrentVersion, 0, "__VR_LOOPBACK_PROBE__", "", 0, null);
            // Try the targeted overload — interpretation may vary (recipient vs exclude).
            try { _netService.SendMessage(new VoiceRouletteMessage(probe), myId); }
            catch (Exception ex) { Godot.GD.Print($"[VR][Net] targeted SendMessage threw: {ex.GetType().Name}"); }
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[VR][Net] self-loopback test failed to set up: {ex.Message}");
        }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _netService.UnregisterMessageHandler(_handler);
    }

    private void HandleMessage(VoiceRouletteMessage msg, ulong senderId)
    {
        Godot.GD.Print($"[VR][Net] bus received: senderId={senderId}");
        var wire = msg.ToWireMessage();
        if (wire is null)
        {
            Godot.GD.PrintErr("[VR][Net] failed to deserialize wire from bus message");
            return;
        }

        // Eat self-loopback probe and log it loudly — if this fires, the bus
        // pipeline is fully working for our type.
        if (wire.Text == "__VR_LOOPBACK_PROBE__")
        {
            Godot.GD.Print($"[VR][Net] ✅ SELF-LOOPBACK PROBE RECEIVED — bus pipeline works end-to-end (senderId={senderId})");
            return;
        }

        // Self-echo filter: STS2's bus delivers our own broadcasts back to us.
        // We compare ulong PlayerIds (more reliable than slot indices, which
        // may both be 0 if the resolver hasn't found NMultiplayerPlayerState).
        var localId = PlayerSlotResolver.ResolveLocalPlayerId();
        if (localId is ulong me && me == senderId)
        {
            Godot.GD.Print($"[VR][Net] skipping self-echo (senderId={senderId} == localPlayerId)");
            return;
        }

        // Resolve sender's stable slot for bubble/audio routing.
        var resolved = PlayerSlotResolver.ResolveSlotFromBusSenderId(senderId);
        if (resolved is byte slot)
        {
            Godot.GD.Print($"[VR][Net] resolved senderId={senderId} → slot={slot}");
            wire = wire with { Sender = slot };
        }
        else
        {
            // Couldn't resolve — but we KNOW it's not self (caught above), so
            // make absolutely sure the dispatcher's slot-based filter doesn't
            // also drop it as a self-echo. Use senderId's low byte as a fake
            // slot — different from local slot in practice.
            var fakeSlot = (byte)(senderId & 0xFF);
            if (localId is ulong me2)
            {
                var localFake = (byte)(me2 & 0xFF);
                if (fakeSlot == localFake) fakeSlot = (byte)(fakeSlot + 1);
            }
            Godot.GD.Print($"[VR][Net] could not resolve senderId={senderId} to slot, using fake slot={fakeSlot} so dispatcher filter doesn't drop it");
            wire = wire with { Sender = fakeSlot };
        }

        LineReceived?.Invoke(wire);
    }

    // -------------------------------------------------------------------------
    // Diagnostic helpers — verify injection worked and act as a control test
    // -------------------------------------------------------------------------

    /// <summary>
    /// After injection, round-trip MessageTypes to verify our type is in the
    /// cache. If the round-trip fails, sending will silently no-op.
    /// </summary>
    private static void VerifyInjection()
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
            var ourId = (int)typeToIdMethod.Invoke(null, new object[] { typeof(VoiceRouletteMessage) })!;
            var args = new object?[] { ourId, null };
            var ok = (bool)tryGetMethod.Invoke(null, args)!;
            var roundTrip = args[1] as Type;
            Godot.GD.Print($"[VR][Net] VerifyInjection: id={ourId} reverse-ok={ok} reverse-type={roundTrip?.FullName ?? "null"}");
            if (!ok || roundTrip != typeof(VoiceRouletteMessage))
                Godot.GD.PrintErr($"[VR][Net] VerifyInjection FAILED — receiving peer won't be able to decode our packets");
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[VR][Net] VerifyInjection threw: {ex.GetType().Name}: {ex.Message}");
        }
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

    /// <summary>
    /// Hardcoded ID for VoiceRouletteMessage. MUST be the same on every peer
    /// running the mod. Otherwise sender's serialized prefix won't match
    /// receiver's lookup, and packets get dropped with no error.
    ///
    /// Why fixed: idToType.Count varies per peer based on game build, mod load
    /// order, and what other types have been touched. Appending at .Count
    /// produced different IDs on each side (e.g. user got 48, teammate could
    /// get 49+) — that's the classic "I send, they don't receive" symptom.
    ///
    /// Choice of 200: well above any built-in count (the type fields _t0.._t48
    /// suggest ≤49 game types). Padding empty slots with a placeholder to
    /// reach this index is cheap (a List of one Type per slot).
    /// </summary>
    private const int VoiceRouletteFixedTypeId = 200;

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

        var preCount = idToType.Count;

        // Pad the list up to the fixed slot. Use a sentinel placeholder Type
        // for empty slots (typeof(object)) — no game code looks these up by
        // ID, so the placeholder is invisible to the rest of the engine.
        while (idToType.Count < VoiceRouletteFixedTypeId)
            idToType.Add(typeof(object));

        if (idToType.Count == VoiceRouletteFixedTypeId)
            idToType.Add(messageType);
        else
            idToType[VoiceRouletteFixedTypeId] = messageType;  // overwrite if already padded

        typeToId[messageType] = VoiceRouletteFixedTypeId;

        Godot.GD.Print($"[VR][Net] InjectIntoMessageTypesCache: pre-count={preCount}, used fixed id={VoiceRouletteFixedTypeId}, post-count={idToType.Count}");
    }
}
