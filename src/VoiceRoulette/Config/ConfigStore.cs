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
        return new ConfigStore(path, schema);
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(Path, JsonSerializer.Serialize(Schema, Options));
    }
}
