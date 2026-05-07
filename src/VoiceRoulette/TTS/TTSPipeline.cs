using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRoulette.TTS;

public sealed class TTSPipeline
{
    private readonly ITTSBackend _backend;
    private readonly AudioCache _cache;

    public TTSPipeline(ITTSBackend backend, AudioCache cache)
    {
        _backend = backend;
        _cache = cache;
    }

    // Returns local mp3 path (cache hit OR fresh synth, both stored in cache/<sha1>.mp3).
    // Throws TTSException on backend errors so the caller can log a specific reason.
    public async Task<string?> SynthesizeToFileAsync(string text, string voice, string? emotion = null, CancellationToken ct = default)
    {
        var key = AudioCache.Key(text, voice, emotion);
        if (_cache.TryGet(key, out var hit))
        {
            Godot.GD.Print($"[VR][TTS] cache HIT key={key[..8]} text='{text}' emotion={emotion ?? "(none)"}");
            return hit;
        }

        Godot.GD.Print($"[VR][TTS] cache MISS key={key[..8]} → calling Doubao");
        using var ms = new MemoryStream();
        await foreach (var ev in _backend.SynthesizeAsync(text, voice, emotion, ct))
        {
            switch (ev)
            {
                case AudioDelta d: ms.Write(d.Bytes); break;
                case DoubaoError e: throw new TTSException($"[{e.Code}] {e.Message}");
                case AudioDone: break;
            }
        }

        if (ms.Length == 0)
        {
            throw new TTSException("backend returned no audio data (0 bytes)");
        }

        _cache.Put(key, ms.ToArray());
        _cache.TryGet(key, out var path);
        Godot.GD.Print($"[VR][TTS] cached key={key[..8]} bytes={ms.Length} path={path}");
        return path;
    }
}

public sealed class TTSException : System.Exception
{
    public TTSException(string msg) : base(msg) { }
}
