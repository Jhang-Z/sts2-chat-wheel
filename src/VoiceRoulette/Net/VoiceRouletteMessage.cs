using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace VoiceRoulette.Net;

/// <summary>
/// STS2 INetMessage wrapper that carries a serialized <see cref="WireMessage"/>.
/// Bytes are embedded as a length-prefixed string so we only need
/// PacketWriter.WriteString / PacketReader.ReadString, avoiding manual bit arithmetic.
/// </summary>
internal sealed class VoiceRouletteMessage : INetMessage
{
    // Stored as a base-64 string so PacketWriter.WriteString can carry arbitrary bytes.
    private string _encodedPayload = string.Empty;

    // Required by INetMessage.
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    internal VoiceRouletteMessage() { }

    internal VoiceRouletteMessage(WireMessage wire)
    {
        _encodedPayload = System.Convert.ToBase64String(WireMessage.Serialize(wire));
    }

    internal WireMessage? ToWireMessage()
    {
        if (string.IsNullOrEmpty(_encodedPayload)) return null;
        try
        {
            var bytes = System.Convert.FromBase64String(_encodedPayload);
            return WireMessage.Deserialize(bytes);
        }
        catch (System.FormatException) { return null; }
    }

    // IPacketSerializable
    public void Serialize(PacketWriter writer) => writer.WriteString(_encodedPayload);

    public void Deserialize(PacketReader reader) => _encodedPayload = reader.ReadString();
}
