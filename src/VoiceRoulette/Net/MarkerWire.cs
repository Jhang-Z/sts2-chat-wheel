using System.Text.Json;

namespace VoiceRoulette.Net;

// Wire format for the "target focus marker" — sender clicks a world point,
// every peer renders an arrow at that point. Coordinates are world-space
// since the battlefield/map scenes lay out identically across peers.
public sealed record MarkerWire(
    byte Version,
    byte Sender,
    float X,
    float Y,
    ulong Timestamp)
{
    public const byte CurrentVersion = 1;

    public static byte[] Serialize(MarkerWire m) =>
        JsonSerializer.SerializeToUtf8Bytes(m);

    public static MarkerWire? Deserialize(byte[] bytes)
    {
        try
        {
            var m = JsonSerializer.Deserialize<MarkerWire>(bytes);
            return m?.Version == CurrentVersion ? m : null;
        }
        catch (JsonException) { return null; }
    }
}
