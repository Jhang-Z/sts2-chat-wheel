namespace VoiceRoulette;

public static class Plugin
{
    public const string Id = "voice_roulette";
    public const string Version = "0.1.0";

    // Real [ModInitializer] hook is wired in Task 15 once we confirm the BaseLib
    // / STS2 attribute path. For now, this class just exposes constants the
    // smoke test verifies.
    public static void Initialize() { /* wired later */ }
    public static void Unload() { /* wired later */ }
}
