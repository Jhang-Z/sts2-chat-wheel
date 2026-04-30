using VoiceRoulette.Lines;
using VoiceRoulette.Net;

namespace VoiceRoulette.Dispatch;

public sealed class Dispatcher
{
    private readonly byte _localSlot;
    private readonly Cooldown _cooldown;
    private readonly INetSync _net;
    private readonly IAudioOutput _audio;
    private readonly IClock _clock;

    public Dispatcher(byte localSlot, Cooldown cooldown, INetSync net, IAudioOutput audio, IClock clock)
    {
        _localSlot = localSlot;
        _cooldown = cooldown;
        _net = net;
        _audio = audio;
        _clock = clock;
        _net.LineReceived += OnReceived;
    }

    public void Send(Line line)
    {
        if (string.IsNullOrEmpty(line.Text)) return;
        if (!_cooldown.TryRecord(_localSlot, _clock.NowSeconds())) return;

        var msg = new WireMessage(
            WireMessage.CurrentVersion, _localSlot, line.Text, line.Voice,
            (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _net.Broadcast(msg);
        _audio.Play(_localSlot, line.Text, line.Voice);
    }

    private void OnReceived(WireMessage m)
    {
        if (m.Sender == _localSlot) return;
        _audio.Play(m.Sender, m.Text, m.Voice);
    }
}
