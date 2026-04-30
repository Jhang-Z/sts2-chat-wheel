using System.IO;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class DoubaoEventParserTests
{
    private static string Read(string n) => File.ReadAllText(Path.Combine("TTS", "Fixtures", n));

    [Fact]
    public void Parse_AudioDelta_ReturnsBytes()
    {
        var ev = DoubaoEventParser.Parse(Read("audio_delta.json"));
        var delta = Assert.IsType<AudioDelta>(ev);
        delta.Bytes.Should().Equal(new byte[] { 0, 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void Parse_AudioDone_ReturnsDoneMarker()
    {
        var ev = DoubaoEventParser.Parse(Read("audio_done.json"));
        Assert.IsType<AudioDone>(ev);
    }

    [Fact]
    public void Parse_Error_CapturesCodeAndMessage()
    {
        var ev = DoubaoEventParser.Parse(Read("error.json"));
        var err = Assert.IsType<DoubaoError>(ev);
        err.Code.Should().Be("invalid_api_key");
        err.Message.Should().Be("bad key");
    }

    [Fact]
    public void Parse_UnknownType_ReturnsUnknown()
    {
        var ev = DoubaoEventParser.Parse("{\"type\":\"weird.event\"}");
        Assert.IsType<UnknownEvent>(ev);
    }
}
