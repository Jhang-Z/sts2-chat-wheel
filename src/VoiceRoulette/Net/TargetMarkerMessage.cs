using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace VoiceRoulette.Net;

/// <summary>
/// Real-time tactical marker — sender clicks a world point, every peer's
/// MarkerOverlay draws an arrow there for ~2s. Different from voice in two
/// ways: ShouldBuffer is false (no point delivering stale markers when a
/// late joiner connects), and the wire format uses MarkerWire not
/// WireMessage.
///
/// MUST be a public class with a public parameterless ctor — game's
/// NetMessageBus.TryDeserializeMessage uses Activator.CreateInstance(type)
/// with publicOnly=true.
/// </summary>
public sealed class TargetMarkerMessage : INetMessage
{
    private string _encodedPayload = string.Empty;

    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;  // markers stale fast; don't deliver to late joiners
    public NetTransferMode Mode => NetTransferMode.Unreliable;  // OK to drop one
    public LogLevel LogLevel => LogLevel.Debug;

    public TargetMarkerMessage() { }

    public TargetMarkerMessage(MarkerWire wire)
    {
        _encodedPayload = System.Convert.ToBase64String(MarkerWire.Serialize(wire));
    }

    public MarkerWire? ToMarkerWire()
    {
        if (string.IsNullOrEmpty(_encodedPayload)) return null;
        try
        {
            var bytes = System.Convert.FromBase64String(_encodedPayload);
            return MarkerWire.Deserialize(bytes);
        }
        catch (System.FormatException) { return null; }
    }

    public void Serialize(PacketWriter writer) => writer.WriteString(_encodedPayload);

    public void Deserialize(PacketReader reader) => _encodedPayload = reader.ReadString();
}
