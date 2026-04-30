using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class TTSPipelineTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"vr-pipe-{System.Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class FakeBackend : ITTSBackend
    {
        private readonly byte[][] _chunks;
        public int CallCount { get; private set; }
        public FakeBackend(params byte[][] chunks) => _chunks = chunks;
        public async IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(
            string text, string voice,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            foreach (var c in _chunks) { yield return new AudioDelta(c); await Task.Yield(); }
            yield return new AudioDone();
        }
    }

    [Fact]
    public async Task Synthesize_CacheMiss_WritesAndReturnsPath()
    {
        var cache = new AudioCache(_dir, 1_000_000);
        var backend = new FakeBackend(new byte[]{1,2}, new byte[]{3,4});
        var pipe = new TTSPipeline(backend, cache);

        var path = await pipe.SynthesizeToFileAsync("hi", "v1");
        path.Should().NotBeNull();
        File.ReadAllBytes(path!).Should().Equal(new byte[]{1,2,3,4});
        backend.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Synthesize_CacheHit_SkipsBackend()
    {
        var cache = new AudioCache(_dir, 1_000_000);
        var backend = new FakeBackend(new byte[]{9});
        var pipe = new TTSPipeline(backend, cache);

        await pipe.SynthesizeToFileAsync("hi", "v1");
        await pipe.SynthesizeToFileAsync("hi", "v1");
        backend.CallCount.Should().Be(1);
    }
}
