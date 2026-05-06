using FluentAssertions;
using VoiceRoulette.Dispatch;
using Xunit;

namespace VoiceRoulette.Tests.Dispatch;

public class CooldownTests
{
    [Fact]
    public void FirstSend_Allowed()
    {
        var c = new Cooldown(perSendSeconds: 1.5, windowMax: 5, windowSeconds: 60);
        c.TryRecord(playerId: 1, nowSeconds: 0).Should().BeTrue();
    }

    [Fact]
    public void WithinPerSend_Blocked()
    {
        var c = new Cooldown(1.5, 5, 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1.0).Should().BeFalse();
    }

    [Fact]
    public void AfterPerSend_Allowed()
    {
        var c = new Cooldown(1.5, 5, 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1.6).Should().BeTrue();
    }

    [Fact]
    public void WindowCap_Enforced()
    {
        var c = new Cooldown(0.0, windowMax: 3, windowSeconds: 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1).Should().BeTrue();
        c.TryRecord(1, 2).Should().BeTrue();
        c.TryRecord(1, 3).Should().BeFalse();
    }

    [Fact]
    public void WindowCap_SlidesOff()
    {
        var c = new Cooldown(0.0, windowMax: 2, windowSeconds: 10);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1).Should().BeTrue();
        c.TryRecord(1, 2).Should().BeFalse();
        c.TryRecord(1, 11).Should().BeTrue(); // first send (t=0) aged out
    }

    [Fact]
    public void DifferentPlayers_Independent()
    {
        var c = new Cooldown(1.5, 5, 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(2, 0).Should().BeTrue();
    }
}
