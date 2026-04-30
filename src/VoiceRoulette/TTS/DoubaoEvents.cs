namespace VoiceRoulette.TTS;

public abstract record DoubaoEvent;
public sealed record AudioDelta(byte[] Bytes) : DoubaoEvent;
public sealed record AudioDone : DoubaoEvent;
public sealed record DoubaoError(string Code, string Message) : DoubaoEvent;
public sealed record UnknownEvent(string Type) : DoubaoEvent;
