using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace VoiceRoulette.SaveLoad;

/// <summary>
/// Reply to a pending SLRequestMessage. Tagged with the proposer's timestamp
/// so a late-arriving vote from a previous proposal can't accidentally count
/// toward a new one.
/// </summary>
public sealed class SLVoteMessage : INetMessage
{
    public ulong VoterId { get; private set; }
    public ulong ProposalTimestamp { get; private set; }
    public bool Accept { get; private set; }

    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public SLVoteMessage() { }

    public SLVoteMessage(ulong voterId, ulong proposalTimestamp, bool accept)
    {
        VoterId = voterId;
        ProposalTimestamp = proposalTimestamp;
        Accept = accept;
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(VoterId, 64);
        writer.WriteULong(ProposalTimestamp, 64);
        writer.WriteBool(Accept);
    }

    public void Deserialize(PacketReader reader)
    {
        VoterId = reader.ReadULong(64);
        ProposalTimestamp = reader.ReadULong(64);
        Accept = reader.ReadBool();
    }
}
