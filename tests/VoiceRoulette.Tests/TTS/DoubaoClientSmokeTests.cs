using System;
using System.Threading;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class DoubaoClientSmokeTests
{
    [Fact]
    public async System.Threading.Tasks.Task RealApi_ReturnsAudioThenDone()
    {
        var key = Environment.GetEnvironmentVariable("DOUBAO_API_KEY");
        if (string.IsNullOrEmpty(key)) return; // skip without secret

        await using var client = new DoubaoClient(
            "wss://ai-gateway.vei.volces.com/v1/realtime?model=doubao-tts", key);

        var gotDelta = false;
        var gotDone  = false;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var ev in client.SynthesizeAsync(
            "你好", "zh_female_kailangjiejie_moon_bigtts", cts.Token))
        {
            if (ev is AudioDelta d) { d.Bytes.Length.Should().BeGreaterThan(0); gotDelta = true; }
            if (ev is AudioDone)    { gotDone = true; }
            if (ev is DoubaoError e) throw new System.Exception($"{e.Code}: {e.Message}");
        }

        gotDelta.Should().BeTrue();
        gotDone.Should().BeTrue();
    }
}
