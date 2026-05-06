using System.Collections.Generic;
using FluentAssertions;
using VoiceRoulette.Dispatch;
using VoiceRoulette.Lines;
using VoiceRoulette.Net;
using Xunit;

namespace VoiceRoulette.Tests.Dispatch;

public class DispatcherTests
{
    private sealed class FakeClock : IClock { public double Now { get; set; } public double NowSeconds() => Now; }
    private sealed class FakeNet : INetSync
    {
        public List<WireMessage> Sent = new();
        public void Broadcast(WireMessage m) => Sent.Add(m);
        public event System.Action<WireMessage>? LineReceived;
        public void Receive(WireMessage m) => LineReceived?.Invoke(m);
    }
    private sealed class FakeAudio : IAudioOutput
    {
        public List<(byte slot, string text, string voice)> Played = new();
        public void Play(byte slot, string text, string voice) => Played.Add((slot, text, voice));
    }

    [Fact]
    public void Send_AllowedByCooldown_BroadcastsAndPlaysLocal()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(localSlot: 2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "hi", "v"));

        net.Sent.Should().HaveCount(1);
        net.Sent[0].Sender.Should().Be(2);
        audio.Played.Should().ContainSingle().Which.text.Should().Be("hi");
    }

    [Fact]
    public void Send_BlockedByCooldown_DoesNothing()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "a", "v"));
        clock.Now = 0.5;
        d.Send(new Line("id", "b", "v"));

        net.Sent.Should().HaveCount(1);
        audio.Played.Should().HaveCount(1);
    }

    [Fact]
    public void Receive_PlaysRemoteWithSenderSlot()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        net.Receive(new WireMessage(1, 5, "x", "v", 0));

        audio.Played.Should().ContainSingle().Which.slot.Should().Be(5);
    }

    [Fact]
    public void Receive_FromSelf_Ignored()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        net.Receive(new WireMessage(1, 2, "x", "v", 0));

        audio.Played.Should().BeEmpty();
    }

    [Fact]
    public void Send_EmptyText_Skipped()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "", "v"));

        net.Sent.Should().BeEmpty();
        audio.Played.Should().BeEmpty();
    }
}
