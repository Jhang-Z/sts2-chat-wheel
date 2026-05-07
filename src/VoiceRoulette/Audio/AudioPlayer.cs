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

    public void Play(byte senderSlot, string text, string voice, string? emotion)
    {
        if (emotion == null)
        {
            GD.Print($"[VR][Audio] Play skipped (text-only): '{text}'");
            return;
        }
        GD.Print($"[VR][Audio] Play: slot={senderSlot}, text='{text}', voice='{voice}', emotion='{emotion}'");
        _ = PlayAsync(senderSlot, text, voice, emotion).ContinueWith(t =>
        {
            if (t.IsFaulted)
                GD.PrintErr($"[VR][Audio] PlayAsync faulted: {t.Exception}");
        }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task PlayAsync(byte senderSlot, string text, string voice, string emotion)
    {
        if (_slots.Length == 0)
        {
            GD.PrintErr("[VR][Audio] no slots available");
            return;
        }

        string? path;
        try
        {
            path = await _tts.SynthesizeToFileAsync(text, voice, emotion);
        }
        catch (TTS.TTSException tex)
        {
            // TTSException carries the server's error code + message verbatim; surface that
            // shape clearly so users can act on it (auth, voice not authorized, quota, etc.).
            GD.PrintErr($"[VR][Audio] TTS rejected '{text}' [{emotion}]: {tex.Message}");
            HintOnCommonErrors(tex.Message);
            return;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[VR][Audio] TTS failed for '{text}' [{emotion}] → {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (path == null)
        {
            GD.PrintErr($"[VR][Audio] TTS returned null path for '{text}'");
            return;
        }

        var fi = File.Exists(path) ? new FileInfo(path) : null;
        GD.Print($"[VR][Audio] resolved path={path}, exists={fi != null}, size={fi?.Length ?? 0}");

        try
        {
            var stream = new AudioStreamMP3 { Data = File.ReadAllBytes(path) };
            var player = _slots[senderSlot % _slots.Length];
            player.Stream = stream;
            player.Play();
            GD.Print($"[VR][Audio] player.Play() bus={player.Bus} playing={player.Playing} vol_db={player.VolumeDb}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[VR][Audio] playback failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void HintOnCommonErrors(string serverMessage)
    {
        var m = serverMessage.ToLowerInvariant();
        if (m.Contains("missing_api_key"))
            GD.PrintErr("[VR][Audio] HINT: open mods/sts2_chat_wheel/data/config.jsonc and set doubao.apiKey.");
        else if (m.Contains("401") || m.Contains("unauthorized") || m.Contains("invalid") && m.Contains("key"))
            GD.PrintErr("[VR][Audio] HINT: API Key invalid or revoked. Re-create one in 火山引擎 → API Key 管理.");
        else if (m.Contains("voice") && (m.Contains("not authorized") || m.Contains("unauthorized") || m.Contains("permission") || m.Contains("授权")))
            GD.PrintErr("[VR][Audio] HINT: voice not authorized. Open 火山引擎 → 音色库 → activate the voice (e.g. shuangkuaisisi).");
        else if (m.Contains("quota") || m.Contains("limit") || m.Contains("额度"))
            GD.PrintErr("[VR][Audio] HINT: quota exhausted. Buy more 字数包 in 火山引擎 console.");
        else if (m.Contains("connect_failed") || m.Contains("network") || m.Contains("dns") || m.Contains("timeout"))
            GD.PrintErr("[VR][Audio] HINT: network issue reaching ai-gateway.vei.volces.com. Check firewall/proxy.");
    }
}
