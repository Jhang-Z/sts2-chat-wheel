using VoiceRoulette.Lines;
using VoiceRoulette.Net;

namespace VoiceRoulette.Dispatch;

public sealed class Dispatcher
{
    private readonly System.Func<byte> _getLocalSlot;
    private readonly Cooldown _cooldown;
    private readonly INetSync _net;
    private readonly IAudioOutput _audio;
    private readonly IClock _clock;

    public Dispatcher(System.Func<byte> getLocalSlot, Cooldown cooldown, INetSync net, IAudioOutput audio, IClock clock)
    {
        _getLocalSlot = getLocalSlot;
        _cooldown = cooldown;
        _net = net;
        _audio = audio;
        _clock = clock;
        _net.LineReceived += OnReceived;
    }

    public void Send(Line line)
    {
        if (string.IsNullOrEmpty(line.Text))
        {
            Godot.GD.Print($"[VR][Dispatch] skipped (empty text)");
            return;
        }
        var localSlot = _getLocalSlot();
        if (!_cooldown.TryRecord(localSlot, _clock.NowSeconds()))
        {
            Godot.GD.Print($"[VR][Dispatch] BLOCKED by cooldown: '{line.Text}'");
            return;
        }

        var msg = new WireMessage(
            WireMessage.CurrentVersion, localSlot, line.Text, line.Voice,
            (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            line.Emotion);
        _net.Broadcast(msg);
        _audio.Play(localSlot, line.Text, line.Voice, line.Emotion);
    }

    private void OnReceived(WireMessage m)
    {
        // Self-echo: bus delivers our own broadcast back to us. We already
        // played + showed it locally in Send(), so skip.
        if (m.Sender == _getLocalSlot()) return;
        _audio.Play(m.Sender, m.Text, m.Voice, m.Emotion);
    }
}
