namespace VoiceRoulette.Lines;

public enum WheelPage { Common, Character, Custom }

public readonly record struct Line(string Id, string Text, string Voice);
