using FluentAssertions;
using VoiceRoulette.Net;
using Xunit;

namespace VoiceRoulette.Tests.Net;

public class WireMessageTests
{
    [Fact]
    public void Roundtrip_PreservesAllFields()
    {
        var msg = new WireMessage(1, 3, "你好", "v_default", 1714560000UL);
        var bytes = WireMessage.Serialize(msg);
        WireMessage? back = WireMessage.Deserialize(bytes);
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Deserialize_VersionMismatch_ReturnsNull()
    {
        var msg = new WireMessage(99, 0, "x", "y", 0);
        var bytes = WireMessage.Serialize(msg);
        Assert.Null(WireMessage.Deserialize(bytes));
    }

    [Fact]
    public void Serialize_Size_FitsInP2PFrame()
    {
        var msg = new WireMessage(1, 0, new string('字', 100), "voice_xyz", 1UL);
        WireMessage.Serialize(msg).Length.Should().BeLessThan(1200);
    }
}
