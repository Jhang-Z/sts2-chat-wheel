using System.Collections.Generic;

namespace VoiceRoulette.Config;

public sealed class ConfigSchema
{
    public string Hotkey { get; set; } = "V";
    public string SettingsHotkey { get; set; } = "Semicolon";
    public DoubaoConfig Doubao { get; set; } = new();
    public string DefaultVoice { get; set; } = "zh_female_kailangjiejie_moon_bigtts";
    public Dictionary<string, string> VoiceMap { get; set; } = new()
    {
        ["ironclad"] = "$default",
        ["silent"] = "$default",
        ["regent"] = "$default",
        ["necrobinder"] = "$default",
        ["defect"] = "$default",
    };
    public PagesConfig Pages { get; set; } = PagesConfig.Defaults();
    public AudioConfig Audio { get; set; } = new();
    public CooldownConfig Cooldown { get; set; } = new();
}

public sealed class DoubaoConfig
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } =
        "wss://ai-gateway.vei.volces.com/v1/realtime?model=doubao-tts";
}

public sealed class CooldownConfig
{
    public double PerSend { get; set; } = 0.5;
    public int WindowMax { get; set; } = 30;
}

public sealed class AudioConfig
{
    public double Volume { get; set; } = 0.8;
    public List<byte> Muted { get; set; } = new();
}

public sealed class PagesConfig
{
    public List<LineEntry> Common { get; set; } = new();
    public List<LineEntry> Custom { get; set; } = new();

    public static PagesConfig Defaults()
    {
        string[] common =
        {
            "好牌！", "打精英怪！", "去休息点", "继续推进",
            "等一下", "撤退！", "我来挡", "干得漂亮！"
        };
        var p = new PagesConfig();
        for (int i = 0; i < 8; i++)
        {
            p.Common.Add(new LineEntry { Id = $"common_{i}", Text = common[i] });
            p.Custom.Add(new LineEntry { Id = $"custom_{i}", Text = "" });
        }
        return p;
    }
}

public sealed class LineEntry
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
