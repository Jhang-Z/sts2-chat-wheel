using System.Text.Json;

namespace VoiceRoulette.Net;

public sealed record WireMessage(
    byte Version,
    byte Sender,
    string Text,
    string Voice,
    ulong Timestamp)
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
