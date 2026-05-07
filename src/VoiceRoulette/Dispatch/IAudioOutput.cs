namespace VoiceRoulette.Dispatch;

public interface IAudioOutput
{
    // emotion=null → text-only line, no audio playback.
    void Play(byte senderSlot, string text, string voice, string? emotion);
}
