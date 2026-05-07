using System.IO;
using FluentAssertions;
using VoiceRoulette.Config;
using Xunit;

namespace VoiceRoulette.Tests.Config;

public class ConfigStoreTests
{
    private string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"vr-{System.Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = ConfigStore.Load(TempPath());
        store.Schema.Hotkey.Should().Be("Y");
        store.Schema.Cooldown.PerSend.Should().Be(0.5);
        store.Schema.Cooldown.WindowMax.Should().Be(30);
        store.Schema.DefaultVoice.Should().Be("zh_female_shuangkuaisisi_uranus_bigtts");
    }

    [Fact]
    public void SaveThenLoad_Roundtrips()
    {
        var path = TempPath();
        var store = ConfigStore.Load(path);
        store.Schema.Hotkey = "B";
        store.Save();

        var reloaded = ConfigStore.Load(path);
        reloaded.Schema.Hotkey.Should().Be("B");
    }

    [Fact]
    public void Load_PartialFile_FillsMissingWithDefaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ \"hotkey\": \"X\" }");
        var store = ConfigStore.Load(path);
        store.Schema.Hotkey.Should().Be("X");
        store.Schema.Cooldown.PerSend.Should().Be(0.5);  // default preserved
    }
}
