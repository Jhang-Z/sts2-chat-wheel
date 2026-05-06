// CONFIRMED mod entry-point (from binary probe of sts2.dll, 2026-05-01):
//
// MegaCrit.Sts2.Core.Modding.ModInitializerAttribute(string initializerMethod) exists
// and is accessible. ModManager.CallModInitializer reflects on loaded assemblies,
// finds classes decorated with [ModInitializer("MethodName")], and invokes the named
// static method. The attribute is applied to the class, not the method.
//
// Usage: [ModInitializer("Initialize")] on the class; Initialize() must be public static void.
//
// UNCONFIRMED gaps (v0.1 stubs):
// - localSlot: no confirmed API for local-player slot index via RunManager.
//   Defaulted to 0 (always works for singleplayer; p2 will have wrong filter).
// - character: no confirmed API for current run character. Defaulted to "ironclad".
//   Voice selection will always use the ironclad voice mapping.
// Both stubs are safe for v0.1 singleplayer + co-op testing.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using VoiceRoulette.Audio;
using VoiceRoulette.Config;
using VoiceRoulette.Dispatch;
using VoiceRoulette.Input;
using VoiceRoulette.Lines;
using VoiceRoulette.Net;
using VoiceRoulette.TTS;
using VoiceRoulette.UI;

namespace VoiceRoulette;

[MegaCrit.Sts2.Core.Modding.ModInitializer("Initialize")]
public static class Plugin
{
    public const string Id = "sts2_chat_wheel";
    public const string Version = "0.1.0";

    private static readonly string ModDir = ResolveModDir();

    // Called by ModManager.CallModInitializer — registered via [ModInitializer("Initialize")]
    // on the Plugin class (see attribute above). Must be public static void.
    public static void Initialize()
    {
        try
        {
            GD.Print($"[VR] Initialize start. ModDir={ModDir}");

            var sceneRoot = ((SceneTree)Engine.GetMainLoop()).Root;
            GD.Print($"[VR] sceneRoot resolved: {sceneRoot?.GetType().Name ?? "null"}");

            var dataDir = Path.Combine(ModDir, "data");
            Directory.CreateDirectory(dataDir);
            // .jsonc extension avoids the game's mod-manifest scanner (.json only).
            var configPath = Path.Combine(dataDir, "config.jsonc");
            var config = ConfigStore.Load(configPath);
            config.Save();
            GD.Print($"[VR] config loaded from {configPath}");

            var registry = new LineRegistry(config.Schema);

            var cacheDir = Path.Combine(ModDir, "cache");
            const long CacheMaxBytes = 100 * 1024 * 1024;
            var cache = new AudioCache(cacheDir, CacheMaxBytes);
            var seedDir = Path.Combine(ModDir, "prerendered");
            cache.SeedFrom(seedDir);
            var seededCount = Directory.Exists(cacheDir)
                ? Directory.GetFiles(cacheDir, "*.mp3").Length : 0;
            GD.Print($"[VR] cache dir={cacheDir}, seed dir={seedDir} (exists={Directory.Exists(seedDir)}), files in cache={seededCount}");

            var doubao = new DoubaoClient(
                config.Schema.Doubao.Endpoint,
                config.Schema.Doubao.ApiKey);
            var tts = new TTSPipeline(doubao, cache);

            var wheel = new WheelUI();
            var audio = new AudioPlayer(tts);
            var bubble = new BubbleOverlay();
            var settings = new SettingsScreen();
            var pinger = new VoiceRoulette.Input.StatusPinger();
            INetSync net = Sts2BusNetSync.TryCreate() ?? (INetSync)new LocalNetSync();
            GD.Print($"[VR] NetSync = {net.GetType().Name}");

            const byte LocalSlot = 0;
            const string Character = "ironclad";

            var dispatcher = new Dispatch.Dispatcher(
                localSlot: LocalSlot,
                new Cooldown(config.Schema.Cooldown.PerSend, config.Schema.Cooldown.WindowMax),
                net, audio, new GodotClock());

            var wheelKey = ParseKey(config.Schema.Hotkey, Key.V);
            var settingsKey = ParseKey(config.Schema.SettingsHotkey, Key.Semicolon);
            GD.Print($"[VR] hotkeys: wheel={wheelKey}, settings={settingsKey}");

            var input = new InputCapture(
                wheelKey, wheel,
                getLineTexts: () => CurrentPageTexts(registry, Character),
                settingsHotkey: settingsKey);

            input.Released += idx =>
            {
                GD.Print($"[VR] Wheel released, sector={idx}");
                if (idx < 0) return;
                var pageSize = config.Schema.Pages.Common.Count;
                if (idx >= pageSize) return;
                var line = registry.Resolve(WheelPage.Common, idx, Character);
                GD.Print($"[VR] Dispatching line: text='{line.Text}' voice='{line.Voice}'");
                dispatcher.Send(line);
                // Show local bubble even when audio plays back: instant feedback.
                bubble.Show(line.Text, LocalSlot);
            };

            // Show bubble for remote players too.
            net.LineReceived += msg =>
            {
                if (msg.Sender == LocalSlot) return;
                bubble.Show(msg.Text, msg.Sender);
            };

            // F2 toggles the settings screen.
            input.SettingsToggled += () => settings.Toggle();

            // Cmd/Ctrl+Click on a potion/power broadcasts a status ping.
            // Goes through dispatcher (cooldown applied) so it shares pacing with voice wheel.
            Action<string> sendPing = text =>
            {
                var line = new Lines.Line("status_ping", text, config.Schema.DefaultVoice);
                dispatcher.Send(line);
                bubble.Show(text, LocalSlot);
            };

            // Defer node attachment to the first idle frame: at mod-load time
            // root Window is mid-setup and rejects AddChild. We connect once,
            // attach our nodes, then disconnect to avoid re-running.
            var tree = (SceneTree)Engine.GetMainLoop();
            Action? attach = null;
            attach = () =>
            {
                try
                {
                    tree.ProcessFrame -= attach;
                    sceneRoot!.AddChild(wheel);
                    sceneRoot.AddChild(audio);
                    sceneRoot.AddChild(input);
                    sceneRoot.AddChild(bubble);
                    sceneRoot.AddChild(settings);
                    sceneRoot.AddChild(pinger);
                    GD.Print($"[VR] nodes attached. wheel.IsInsideTree={wheel.IsInsideTree()}, input.IsInsideTree={input.IsInsideTree()}");

                    Func<string, bool> hasAudioFn = text =>
                    {
                        if (string.IsNullOrEmpty(text)) return false;
                        var key = TTS.AudioCache.Key(text, config.Schema.DefaultVoice);
                        return cache.TryGet(key, out _);
                    };
                    // Manual lifecycle since Godot source generators don't run for this project.
                    wheel.Initialize(modDir: ModDir, hasAudio: hasAudioFn);
                    input.StartPolling();
                    bubble.StartPolling();
                    settings.Initialize(config,
                        onSaved: () =>
                        {
                            GD.Print("[VR] settings saved; wheel will reflect new lines.");
                        },
                        toggleKeyHint: KeyToHint(settingsKey),
                        hasAudio: hasAudioFn);
                    pinger.Start(sendPing);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[VR] deferred attach failed: {ex}");
                }
            };
            tree.ProcessFrame += attach;
            GD.Print("[VR] Initialize complete — node attach scheduled for first ProcessFrame.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR] Initialize FAILED: {ex}");
        }
    }

    public static void Unload()
    {
        // Godot nodes auto-free when removed from the scene tree.
        // Sts2BusNetSync is IDisposable; if we held a reference we'd dispose here.
        // v0.1: no explicit cleanup needed beyond node lifecycle.
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IList<string> CurrentPageTexts(LineRegistry registry, string character)
    {
        // v0.1: always show Common page. A future task can add per-character pages.
        var texts = new List<string>();
        const int SectorCount = 8;
        for (var i = 0; i < SectorCount; i++)
        {
            try
            {
                var line = registry.Resolve(WheelPage.Common, i, character);
                texts.Add(line.Text);
            }
            catch
            {
                texts.Add(string.Empty);
            }
        }
        return texts;
    }

    private static Key ParseKey(string s, Key fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return Enum.TryParse<Key>(s, ignoreCase: true, out var k) ? k : fallback;
    }

    private static string KeyToHint(Key k) => k switch
    {
        Key.Semicolon => ";",
        Key.Apostrophe => "'",
        Key.Backslash => "\\",
        Key.Bracketleft => "[",
        Key.Bracketright => "]",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        Key.Quoteleft => "`",
        _ => k.ToString(),
    };

    private static string ResolveModDir()
    {
        // The mod DLL lives in <mods_dir>/voice_roulette/VoiceRoulette.dll.
        // Walk up one level to get the mod directory.
        var dllPath = typeof(Plugin).Assembly.Location;
        return Path.GetDirectoryName(dllPath)
            ?? AppContext.BaseDirectory;
    }
}

// -------------------------------------------------------------------------
// GodotClock — IClock implementation backed by Godot's monotonic timer.
// Placed here to keep it out of the shared Dispatch layer (Godot dependency).
// -------------------------------------------------------------------------
file sealed class GodotClock : IClock
{
    public double NowSeconds() => Time.GetTicksMsec() / 1000.0;
}
