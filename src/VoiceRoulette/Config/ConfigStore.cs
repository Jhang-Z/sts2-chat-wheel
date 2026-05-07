using System.IO;
using System.Text.Json;

namespace VoiceRoulette.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ConfigSchema Schema { get; private set; }
    public string Path { get; }

    private ConfigStore(string path, ConfigSchema schema)
    {
        Path = path;
        Schema = schema;
    }

    // Wheel slot count must match SettingsScreen.LineCount and WheelUI.SectorCount*Rings.
    private const int WheelSlotCount = 16;

    public static ConfigStore Load(string path)
    {
        if (!File.Exists(path))
            return new ConfigStore(path, new ConfigSchema());

        var json = File.ReadAllText(path);
        ConfigSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<ConfigSchema>(json, Options)
                     ?? new ConfigSchema();
        }
        catch (JsonException)
        {
            schema = new ConfigSchema();
        }
        MigrateLines(schema);
        return new ConfigStore(path, schema);
    }

    /// <summary>
    /// Old configs (pre 2-ring) had 8 slots. New schema has 16 (inner+outer
    /// rings). Pad missing slots with the new defaults so old users get the
    /// outer ring filled out without losing any of their existing 8.
    /// </summary>
    private static void MigrateLines(ConfigSchema schema)
    {
        if (schema.Lines.Count >= WheelSlotCount) return;
        var defaults = LineEntry.Defaults();
        while (schema.Lines.Count < WheelSlotCount)
        {
            var i = schema.Lines.Count;
            var fallback = i < defaults.Count
                ? defaults[i]
                : new LineEntry { Id = $"slot_{i}", Text = "" };
            schema.Lines.Add(new LineEntry
            {
                Id = $"slot_{i}",
                Text = fallback.Text,
                Emotion = fallback.Emotion,
            });
        }
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(Path, JsonSerializer.Serialize(Schema, Options));
    }
}
