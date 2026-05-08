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
        public List<MarkerWire> SentMarkers = new();
        public void Broadcast(WireMessage m) => Sent.Add(m);
        public void BroadcastMarker(MarkerWire m) => SentMarkers.Add(m);
        public event System.Action<WireMessage>? LineReceived;
        public event System.Action<MarkerWire>? MarkerReceived;
        public void Receive(WireMessage m) => LineReceived?.Invoke(m);
        public void ReceiveMarker(MarkerWire m) => MarkerReceived?.Invoke(m);
    }
    private sealed class FakeAudio : IAudioOutput
    {
        public List<(byte slot, string text, string voice, string? emotion)> Played = new();
        public void Play(byte slot, string text, string voice, string? emotion) => Played.Add((slot, text, voice, emotion));
    }

    [Fact(Skip = "Calls into Dispatcher → Godot.GD.Print → needs game runtime")]
    public void Send_AllowedByCooldown_BroadcastsAndPlaysLocal()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(getLocalSlot: () => (byte)2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "hi", "v", null));

        net.Sent.Should().HaveCount(1);
        net.Sent[0].Sender.Should().Be(2);
        audio.Played.Should().ContainSingle().Which.text.Should().Be("hi");
    }

    [Fact(Skip = "Calls into Dispatcher → Godot.GD.Print → needs game runtime")]
    public void Send_BlockedByCooldown_DoesNothing()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(() => (byte)2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "a", "v", null));
        clock.Now = 0.5;
        d.Send(new Line("id", "b", "v", null));

        net.Sent.Should().HaveCount(1);
        audio.Played.Should().HaveCount(1);
    }

    [Fact(Skip = "Calls into Dispatcher → Godot.GD.Print → needs game runtime")]
    public void Receive_PlaysRemoteWithSenderSlot()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(() => (byte)2, new Cooldown(1.5, 5), net, audio, clock);

        net.Receive(new WireMessage(1, 5, "x", "v", 0));

        audio.Played.Should().ContainSingle().Which.slot.Should().Be(5);
    }

    [Fact(Skip = "Calls into Dispatcher → Godot.GD.Print → needs game runtime")]
    public void Receive_FromSelf_Ignored()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(() => (byte)2, new Cooldown(1.5, 5), net, audio, clock);

        net.Receive(new WireMessage(1, 2, "x", "v", 0));

        audio.Played.Should().BeEmpty();
    }

    [Fact(Skip = "Calls into Dispatcher → Godot.GD.Print → needs game runtime")]
    public void Send_EmptyText_Skipped()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(() => (byte)2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "", "v", null));

        net.Sent.Should().BeEmpty();
        audio.Played.Should().BeEmpty();
    }
}
