using System.Text.Json;

namespace VoiceRoulette.Net;

// Emotion is nullable. Older v1 peers serialize without it; new clients deserialize
// missing field as null = text-only. This is forward/backward compatible without a
// version bump — receivers who don't understand emotion just play no audio.
public sealed record WireMessage(
    byte Version,
    byte Sender,
    string Text,
    string Voice,
    ulong Timestamp,
    string? Emotion = null)
{
    public const byte CurrentVersion = 1;

    public static byte[] Serialize(WireMessage m) =>
        JsonSerializer.SerializeToUtf8Bytes(m);

    public static WireMessage? Deserialize(byte[] bytes)
    {
        try
        {
            var m = JsonSerializer.Deserialize<WireMessage>(bytes);
            return m?.Version == CurrentVersion ? m : null;
        }
        catch (JsonException) { return null; }
    }
}
