using MessagePack;

namespace VoiceRoulette.Net;

[MessagePackObject]
public sealed record WireMessage(
    [property: Key(0)] byte Version,
    [property: Key(1)] byte Sender,
    [property: Key(2)] string Text,
    [property: Key(3)] string Voice,
    [property: Key(4)] ulong Timestamp)
{
    public const byte CurrentVersion = 1;

    public static byte[] Serialize(WireMessage m) => MessagePackSerializer.Serialize(m);

    public static WireMessage? Deserialize(byte[] bytes)
    {
        try
        {
            var m = MessagePackSerializer.Deserialize<WireMessage>(bytes);
            return m.Version == CurrentVersion ? m : null;
        }
        catch (MessagePackSerializationException) { return null; }
    }
}
