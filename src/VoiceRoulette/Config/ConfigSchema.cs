using System.Collections.Generic;

namespace VoiceRoulette.Config;

public sealed class ConfigSchema
{
    public string Hotkey { get; set; } = "V";
    public string SettingsHotkey { get; set; } = "Semicolon";
    public DoubaoConfig Doubao { get; set; } = new();
    // Doubao TTS 2.0 — uranus voices (爽快思思·女声·轻快). Emotion is controlled by
    // an inline bracketed prompt prepended to the text (handled in DoubaoClient), NOT by
    // a separate API field. See DoubaoClient.EmotionToPrefix.
    public string DefaultVoice { get; set; } = "zh_female_shuangkuaisisi_uranus_bigtts";
    public List<LineEntry> Lines { get; set; } = LineEntry.Defaults();
    public List<LibraryEntry> Library { get; set; } = LibraryEntry.Defaults();
    public AudioConfig Audio { get; set; } = new();
    public CooldownConfig Cooldown { get; set; } = new();
}

public sealed class DoubaoConfig
{
    // V3 WebSocket endpoint, "新版控制台" auth (API Key only, NO App ID).
    // Doc: https://www.volcengine.com/docs/6561/1719100
    //   Headers:  X-Api-Key, X-Api-Resource-Id, X-Api-Request-Id (optional)
    //   Resource: seed-tts-2.0  → 豆包语音合成模型2.0 (uranus voices)
    //             seed-tts-1.0  → 豆包语音合成模型1.0 (moon voices)
    public string ApiKey { get; set; } = "";
    public string ResourceId { get; set; } = "seed-tts-2.0";
    public string Endpoint { get; set; } = "wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream";
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

// One entry in the user-editable phrase library (语音库) shown on the right
// side of the settings page. Users can add and delete entries; presets ship
// in Defaults().
public sealed class LibraryEntry
{
    public string Text { get; set; } = "";
    public string Category { get; set; } = "战斗";
    public string? Emotion { get; set; }   // null = text-only

    public static List<LibraryEntry> Defaults()
    {
        var list = new List<LibraryEntry>();
        foreach (var e in VoiceRoulette.Lines.LineLibrary.All)
            list.Add(new LibraryEntry { Text = e.Text, Category = e.Category, Emotion = e.DefaultEmotion });
        return list;
    }
}

// One slot in the 8-sector wheel.
//   Emotion == null  → text-only (bubble shows, no audio)
//   Emotion != null  → synthesize with that emotion (novel_dialog | happy | angry | sad | sorry)
public sealed class LineEntry
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Emotion { get; set; }

    public static List<LineEntry> Defaults()
    {
        // Sensible default mix: a few voiced (with matching emotion) and a few text-only,
        // so users immediately see the toggle in action.
        return new()
        {
            new() { Id = "slot_0", Text = "好牌！",     Emotion = "happy" },
            new() { Id = "slot_1", Text = "打精英怪！", Emotion = "novel_dialog" },
            new() { Id = "slot_2", Text = "去休息点",   Emotion = null },
            new() { Id = "slot_3", Text = "继续推进",   Emotion = "novel_dialog" },
            new() { Id = "slot_4", Text = "等一下",     Emotion = null },
            new() { Id = "slot_5", Text = "撤退！",     Emotion = "angry" },
            new() { Id = "slot_6", Text = "我来挡",     Emotion = "novel_dialog" },
            new() { Id = "slot_7", Text = "干得漂亮！", Emotion = "happy" },
        };
    }
}
