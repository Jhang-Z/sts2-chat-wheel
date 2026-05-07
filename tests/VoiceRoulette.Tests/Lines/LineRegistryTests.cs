using FluentAssertions;
using VoiceRoulette.Config;
using VoiceRoulette.Lines;
using Xunit;

namespace VoiceRoulette.Tests.Lines;

public class LineRegistryTests
{
    private static LineRegistry Build()
    {
        var schema = new ConfigSchema();
        return new LineRegistry(schema);
    }

    [Fact]
    public void Resolve_FirstSlot_ReturnsDefaultText()
    {
        var line = Build().Resolve(0);
        line.Text.Should().Be("好牌！");
        line.Voice.Should().Be("zh_female_shuangkuaisisi_uranus_bigtts");
        line.Emotion.Should().Be("happy");
    }

    [Fact]
    public void Resolve_TextOnlySlot_HasNullEmotion()
    {
        // Default at index 2 is "去休息点" with Emotion=null (text-only).
        var line = Build().Resolve(2);
        line.Emotion.Should().BeNull();
    }

    [Fact]
    public void Resolve_VoiceComesFromDefaultVoice()
    {
        var schema = new ConfigSchema { DefaultVoice = "voice_x" };
        new LineRegistry(schema).Resolve(3).Voice.Should().Be("voice_x");
    }

    [Fact]
    public void Resolve_OutOfRangeSector_Throws()
    {
        var act = () => Build().Resolve(99);
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }
}
