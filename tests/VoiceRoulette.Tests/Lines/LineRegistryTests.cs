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
        line.Text.Should().Be("我有啥招呢");
        line.Voice.Should().Be("zh_female_shuangkuaisisi_uranus_bigtts");
        line.Emotion.Should().Be("sorry");
    }

    [Fact]
    public void Resolve_HappySlot_HasHappyEmotion()
    {
        // Default at index 7 is "好牌,太强了!!!" with Emotion=happy.
        var line = Build().Resolve(7);
        line.Emotion.Should().Be("happy");
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
