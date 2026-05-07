namespace VoiceRoulette.Lines;

public enum WheelPage { Common }

// Emotion = null  → text-only, no voice playback.
// Emotion != null → synthesize with this emotion (e.g. "happy", "novel_dialog").
public readonly record struct Line(string Id, string Text, string Voice, string? Emotion);
