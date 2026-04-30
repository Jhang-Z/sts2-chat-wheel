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

    public async Task<string?> SynthesizeToFileAsync(string text, string voice, CancellationToken ct = default)
    {
        var key = AudioCache.Key(text, voice);
        if (_cache.TryGet(key, out var hit)) return hit;

        using var ms = new MemoryStream();
        await foreach (var ev in _backend.SynthesizeAsync(text, voice, ct))
        {
            switch (ev)
            {
                case AudioDelta d: ms.Write(d.Bytes); break;
                case DoubaoError e: throw new TTSException($"{e.Code}: {e.Message}");
                case AudioDone: break;
            }
        }
        _cache.Put(key, ms.ToArray());
        _cache.TryGet(key, out var path);
        return path;
    }
}

public sealed class TTSException : System.Exception
{
    public TTSException(string msg) : base(msg) { }
}
