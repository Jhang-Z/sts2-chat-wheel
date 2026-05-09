using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace VoiceRoulette.SaveLoad;

/// <summary>
/// Broadcast when a peer presses the SL hotkey. Every peer (including the
/// proposer) shows a vote prompt on receipt. ShouldBuffer=false because a
/// stale SL proposal delivered to a late joiner is meaningless.
///
/// MUST be a public class with a public parameterless ctor — game's
/// NetMessageBus.TryDeserializeMessage uses Activator.CreateInstance(type)
/// with publicOnly=true.
/// </summary>
public sealed class SLRequestMessage : INetMessage
{
    public ulong ProposerId { get; private set; }
    public ulong Timestamp { get; private set; }

    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public SLRequestMessage() { }

    public SLRequestMessage(ulong proposerId, ulong timestamp)
    {
        ProposerId = proposerId;
        Timestamp = timestamp;
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(ProposerId, 64);
        writer.WriteULong(Timestamp, 64);
    }

    public void Deserialize(PacketReader reader)
    {
        ProposerId = reader.ReadULong(64);
        Timestamp = reader.ReadULong(64);
    }
}
