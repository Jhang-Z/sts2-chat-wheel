using System;
using System.Collections.Generic;
using VoiceRoulette.Config;

namespace VoiceRoulette.Lines;

public sealed class LineRegistry
{
    private readonly ConfigSchema _schema;

    public LineRegistry(ConfigSchema schema) => _schema = schema;

    public Line Resolve(WheelPage page, int sectorIdx, string character)
    {
        var entries = page switch
        {
            WheelPage.Common => _schema.Pages.Common,
            WheelPage.Custom => _schema.Pages.Custom,
            WheelPage.Character => GetCharacterLines(character),
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };

        if (sectorIdx < 0 || sectorIdx >= entries.Count)
            throw new ArgumentOutOfRangeException(nameof(sectorIdx));

        var entry = entries[sectorIdx];
        var voice = ResolveVoice(character);
        return new Line(entry.Id, entry.Text, voice);
    }

    private string ResolveVoice(string character)
    {
        if (_schema.VoiceMap.TryGetValue(character, out var v) && v != "$default")
            return v;
        return _schema.DefaultVoice;
    }

    private static List<LineEntry> GetCharacterLines(string character)
    {
        // v1: character page mirrors common until per-character lines are authored
        return PagesConfig.Defaults().Common;
    }
}
