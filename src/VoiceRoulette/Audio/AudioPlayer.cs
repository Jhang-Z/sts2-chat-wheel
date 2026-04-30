using System.IO;
using System.Threading.Tasks;
using Godot;
using VoiceRoulette.Dispatch;
using VoiceRoulette.TTS;

namespace VoiceRoulette.Audio;

public sealed partial class AudioPlayer : Node, IAudioOutput
{
    private readonly TTSPipeline _tts;
    private readonly AudioStreamPlayer[] _slots;
    private const string BusName = "VoiceRoulette";

    public AudioPlayer(TTSPipeline tts, int maxSlots = 4)
    {
        _tts = tts;
        EnsureBus();
        _slots = new AudioStreamPlayer[maxSlots];
        for (int i = 0; i < maxSlots; i++)
        {
            var p = new AudioStreamPlayer { Bus = BusName };
            AddChild(p);
            _slots[i] = p;
        }
    }

    private static void EnsureBus()
    {
        if (AudioServer.GetBusIndex(BusName) >= 0) return;
        AudioServer.AddBus();
        AudioServer.SetBusName(AudioServer.BusCount - 1, BusName);
    }

    public void Play(byte senderSlot, string text, string voice)
    {
        _ = PlayAsync(senderSlot, text, voice);
    }

    private async Task PlayAsync(byte senderSlot, string text, string voice)
    {
        var path = await _tts.SynthesizeToFileAsync(text, voice);
        if (path == null) return;

        var stream = new AudioStreamMP3 { Data = File.ReadAllBytes(path) };
        var player = _slots[senderSlot % _slots.Length];
        player.Stream = stream;
        player.Play();
    }
}
