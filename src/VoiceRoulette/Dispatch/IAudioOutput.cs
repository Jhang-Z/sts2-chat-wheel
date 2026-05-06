namespace VoiceRoulette.Dispatch;

public interface IAudioOutput
{
    void Play(byte senderSlot, string text, string voice);
}
