using System.IO;
using System.Text;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class AudioCacheTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"vr-cache-{System.Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Key_IsStableForSameInputs()
    {
        AudioCache.Key("hi", "v1").Should().Be(AudioCache.Key("hi", "v1"));
        AudioCache.Key("hi", "v1").Should().NotBe(AudioCache.Key("hi", "v2"));
    }

    [Fact]
    public void TryGet_Miss_ReturnsFalse()
    {
        var c = new AudioCache(_dir, maxBytes: 1_000_000);
        c.TryGet(AudioCache.Key("nope", "v"), out var path).Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void Put_ThenGet_ReturnsPath()
    {
        var c = new AudioCache(_dir, maxBytes: 1_000_000);
        var key = AudioCache.Key("hello", "v");
        c.Put(key, Encoding.UTF8.GetBytes("audio-bytes"));
        c.TryGet(key, out var path).Should().BeTrue();
        File.ReadAllText(path!).Should().Be("audio-bytes");
    }

    [Fact]
    public void Put_OverCapacity_EvictsOldest()
    {
        var c = new AudioCache(_dir, maxBytes: 200);
        c.Put(AudioCache.Key("a", "v"), new byte[100]);
        System.Threading.Thread.Sleep(10);
        c.Put(AudioCache.Key("b", "v"), new byte[100]);
        System.Threading.Thread.Sleep(10);
        c.Put(AudioCache.Key("c", "v"), new byte[100]); // forces eviction

        c.TryGet(AudioCache.Key("a", "v"), out _).Should().BeFalse();
        c.TryGet(AudioCache.Key("b", "v"), out _).Should().BeTrue();
        c.TryGet(AudioCache.Key("c", "v"), out _).Should().BeTrue();
    }
}
