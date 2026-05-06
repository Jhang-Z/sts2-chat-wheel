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
    public void Resolve_CommonPage_ReturnsExpectedText()
    {
        var line = Build().Resolve(WheelPage.Common, sectorIdx: 0, character: "ironclad");
        line.Text.Should().Be("好牌！");
        line.Voice.Should().Be("zh_female_kailangjiejie_moon_bigtts");
    }

    [Fact]
    public void Resolve_VoiceMapDollarDefault_ResolvesToDefaultVoice()
    {
        var schema = new ConfigSchema { DefaultVoice = "voice_x" };
        var reg = new LineRegistry(schema);
        reg.Resolve(WheelPage.Common, 3, "silent").Voice.Should().Be("voice_x");
    }

    [Fact]
    public void Resolve_OutOfRangeSector_Throws()
    {
        var act = () => Build().Resolve(WheelPage.Common, 99, "ironclad");
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Resolve_CustomEmptySlot_ReturnsEmptyTextAllowingDispatcherToSkip()
    {
        Build().Resolve(WheelPage.Custom, 0, "ironclad").Text.Should().Be("");
    }
}
