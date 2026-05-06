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
        GD.Print($"[VR][Audio] Play called: slot={senderSlot}, text='{text}', voice='{voice}', isInTree={IsInsideTree()}");
        _ = PlayAsync(senderSlot, text, voice).ContinueWith(t =>
        {
            if (t.IsFaulted)
                GD.PrintErr($"[VR][Audio] PlayAsync faulted: {t.Exception}");
        }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task PlayAsync(byte senderSlot, string text, string voice)
    {
        if (_slots.Length == 0)
        {
            GD.PrintErr("[VR][Audio] no slots available");
            return;
        }

        string? path;
        try
        {
            path = await _tts.SynthesizeToFileAsync(text, voice);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[VR][Audio] TTS failed for '{text}': {ex.Message}");
            return;
        }

        if (path == null)
        {
            GD.PrintErr($"[VR][Audio] TTS returned null path for '{text}'");
            return;
        }

        GD.Print($"[VR][Audio] resolved path={path}, exists={File.Exists(path)}, size={(File.Exists(path) ? new FileInfo(path).Length : 0)}");

        try
        {
            var stream = new AudioStreamMP3 { Data = File.ReadAllBytes(path) };
            var player = _slots[senderSlot % _slots.Length];
            player.Stream = stream;
            player.Play();
            GD.Print($"[VR][Audio] player.Play() called. bus={player.Bus}, playing={player.Playing}, vol_db={player.VolumeDb}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[VR][Audio] playback failed: {ex}");
        }
    }
}
