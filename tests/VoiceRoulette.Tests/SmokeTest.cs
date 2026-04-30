using FluentAssertions;
using Xunit;

namespace VoiceRoulette.Tests;

public class SmokeTest
{
    [Fact]
    public void PluginVersion_IsNotEmpty()
    {
        Plugin.Version.Should().NotBeNullOrWhiteSpace();
    }
}
