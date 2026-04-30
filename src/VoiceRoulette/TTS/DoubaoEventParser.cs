using System;
using System.Text.Json;

namespace VoiceRoulette.TTS;

public static class DoubaoEventParser
{
    public static DoubaoEvent Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString() ?? "";

        return type switch
        {
            "response.audio.delta" => new AudioDelta(
                Convert.FromBase64String(root.GetProperty("delta").GetString() ?? "")),
            "response.audio.done"  => new AudioDone(),
            "error" => new DoubaoError(
                root.GetProperty("error").GetProperty("code").GetString() ?? "",
                root.GetProperty("error").GetProperty("message").GetString() ?? ""),
            _ => new UnknownEvent(type),
        };
    }
}
