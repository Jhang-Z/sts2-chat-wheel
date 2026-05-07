using System.Collections.Generic;
using System.Threading;

namespace VoiceRoulette.TTS;

public interface ITTSBackend
{
    IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(string text, string voice, string? emotion = null, CancellationToken ct = default);
}
