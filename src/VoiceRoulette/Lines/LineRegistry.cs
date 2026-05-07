using System;
using VoiceRoulette.Config;

namespace VoiceRoulette.Lines;

public sealed class LineRegistry
{
    private readonly ConfigSchema _schema;

    public LineRegistry(ConfigSchema schema) => _schema = schema;

    public Line Resolve(int sectorIdx)
    {
        var lines = _schema.Lines;
        if (sectorIdx < 0 || sectorIdx >= lines.Count)
            throw new ArgumentOutOfRangeException(nameof(sectorIdx));

        var entry = lines[sectorIdx];
        return new Line(entry.Id, entry.Text, _schema.DefaultVoice, entry.Emotion);
    }
}
