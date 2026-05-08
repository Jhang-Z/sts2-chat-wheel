// CONFIRMED mod entry-point (binary probe of sts2.dll, 2026-05-01):
// MegaCrit.Sts2.Core.Modding.ModInitializerAttribute("Initialize") on the class
// + public static void Initialize() method.
//
// v0.2 changes (this rewrite):
//   - Single voice (zh_female_shuangkuaisisi_moon_bigtts), no per-character map.
//   - Per-line voice toggle + emotion dropdown (设置页里).
//   - Status pings (易伤/虚弱、potion/power) fixed to emotion=novel_dialog.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using VoiceRoulette.Audio;
using VoiceRoulette.Combat;
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
    public const string Version = "0.2.0";

    private const string PingEmotion = "novel_dialog"; // status pings always speak in 平和

    private static readonly string ModDir = ResolveModDir();

    public static void Initialize()
    {
        try
        {
            GD.Print($"[VR] Initialize start. ModDir={ModDir}");

            var sceneRoot = ((SceneTree)Engine.GetMainLoop()).Root;
            GD.Print($"[VR] sceneRoot resolved: {sceneRoot?.GetType().Name ?? "null"}");

            var dataDir = Path.Combine(ModDir, "data");
            Directory.CreateDirectory(dataDir);
            var configPath = Path.Combine(dataDir, "config.jsonc");
            var config = ConfigStore.Load(configPath);
            config.Save();
            GD.Print($"[VR] config loaded from {configPath}");

            var registry = new LineRegistry(config.Schema);

            var cacheDir = Path.Combine(ModDir, "cache");
            const long CacheMaxBytes = 100 * 1024 * 1024;
            var cache = new AudioCache(cacheDir, CacheMaxBytes);
            // No prerendered seeding in v0.2 — voice + emotion combination space is
            // too large to ship offline. Doubao TTS synthesizes on first use, then caches.

            var doubao = new DoubaoClient(config.Schema.Doubao);
            var tts = new TTSPipeline(doubao, cache);

            var wheel = new WheelUI();
            var audio = new AudioPlayer(tts);
            var bubble = new BubbleOverlay();
            var marker = new MarkerOverlay();
            var settings = new SettingsScreen();
            var pinger = new StatusPinger();
            var analyzer = new HandAnalyzer();
            var threat = new ThreatAnalyzer();
            var markerInput = new VoiceRoulette.Input.MarkerInput();
            INetSync net = new AdaptiveNetSync();
            GD.Print($"[VR] NetSync = {net.GetType().Name}");

            // Dynamically resolve our slot every time we send. In singleplayer
            // this returns 0; in co-op it returns whichever slot STS2 assigned
            // us (could be 0/1/2/3 depending on join order). Caching once at
            // mod load doesn't work because the mod loads before any session.
            Func<byte> getLocalSlot = () => PlayerSlotResolver.ResolveLocalSlot();

            var dispatcher = new Dispatch.Dispatcher(
                getLocalSlot: getLocalSlot,
                new Cooldown(config.Schema.Cooldown.PerSend, config.Schema.Cooldown.WindowMax),
                net, audio, new GodotClock());

            var wheelKey = ParseKey(config.Schema.Hotkey, Key.V);
            var settingsKey = ParseKey(config.Schema.SettingsHotkey, Key.Semicolon);
            GD.Print($"[VR] hotkeys: wheel={wheelKey}, settings={settingsKey}");

            var input = new InputCapture(
                wheelKey, wheel,
                getLineTexts: () => CurrentPageView(config.Schema.Lines),
                settingsHotkey: settingsKey);

            input.Released += idx =>
            {
                GD.Print($"[VR] Wheel released, sector={idx}");
                if (idx < 0 || idx >= config.Schema.Lines.Count) return;
                var entry = config.Schema.Lines[idx];
                if (string.IsNullOrEmpty(entry.Text)) return;
                var line = registry.Resolve(idx);
                GD.Print($"[VR] Dispatching: text='{line.Text}' emotion='{line.Emotion ?? "(none)"}'");
                dispatcher.Send(line);
                bubble.Show(line.Text, getLocalSlot(), hasVoice: line.Emotion != null);
            };

            net.LineReceived += msg =>
            {
                if (msg.Sender == getLocalSlot()) return;
                bubble.Show(msg.Text, msg.Sender, hasVoice: msg.Emotion != null);
            };

            net.MarkerReceived += m =>
            {
                if (m.Sender == getLocalSlot()) return;  // we already drew our own
                marker.Show(new Vector2(m.X, m.Y));
            };

            input.SettingsToggled += () => settings.Toggle();

            // Status pings (potion/power click, hand analysis) always go through dispatcher
            // with fixed emotion=novel_dialog so they share cooldown with the wheel.
            Action<string> sendPing = text =>
            {
                var line = new Line("status_ping", text, config.Schema.DefaultVoice, PingEmotion);
                dispatcher.Send(line);
                bubble.Show(text, getLocalSlot(), hasVoice: true);
            };

            // Settings preview: synthesize and play locally without going through
            // the dispatcher (no cooldown, no broadcast, just so the user can hear what they configured).
            // Voice comes from the SettingsScreen's current dropdown selection — not config —
            // so changes audition before the user hits Save.
            Action<string, string?, string> preview = (text, emotion, voice) =>
            {
                if (string.IsNullOrEmpty(text)) return;
                audio.Play(getLocalSlot(), text, voice, emotion);
            };

            // Defer node attachment to first idle frame: at mod-load time the root Window
            // is mid-setup and rejects AddChild.
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
                    sceneRoot.AddChild(marker);
                    sceneRoot.AddChild(settings);
                    sceneRoot.AddChild(pinger);
                    sceneRoot.AddChild(analyzer);
                    sceneRoot.AddChild(threat);
                    sceneRoot.AddChild(markerInput);
                    GD.Print($"[VR] nodes attached.");

                    wheel.Initialize(modDir: ModDir);
                    input.StartPolling();
                    bubble.StartPolling();
                    settings.Initialize(config,
                        onSaved: () =>
                        {
                            // Re-parse hotkeys from saved config and rebind live —
                            // user expects new keys to work immediately, no restart.
                            var newWheel = ParseKey(config.Schema.Hotkey, Key.V);
                            var newSettings = ParseKey(config.Schema.SettingsHotkey, Key.Semicolon);
                            input.Rebind(newWheel, newSettings);
                            GD.Print($"[VR] settings saved. hotkeys: wheel={newWheel} settings={newSettings}");
                        },
                        previewCallback: preview,
                        toggleKeyHint: KeyToHint(settingsKey));
                    pinger.Start(sendPing);
                    analyzer.Start(getLocalSlot(), sendPing);
                    threat.Start(sendPing);
                    marker.Start();
                    markerInput.Start(enemyPos =>
                    {
                        // F+click on enemy: combine three things into one
                        // gesture — local arrow, network broadcast, voice
                        // ping (bubble + audio), and the game's UI click sfx.
                        var slot = getLocalSlot();
                        marker.Show(enemyPos);
                        var wire = new MarkerWire(
                            MarkerWire.CurrentVersion,
                            slot,
                            enemyPos.X, enemyPos.Y,
                            (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        net.BroadcastMarker(wire);
                        sendPing("都打这个");
                        try
                        {
                            MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager.Instance?
                                .PlayOneShot("event:/sfx/ui/clicks/ui_click", 1.0f);
                        }
                        catch (Exception ex)
                        {
                            GD.PrintErr($"[VR][Marker] sfx failed: {ex.Message}");
                        }
                    });
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
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const int WheelSlotCount = 16;

    private static (IList<string> texts, IList<bool> hasVoice) CurrentPageView(IList<LineEntry> lines)
    {
        var texts = new List<string>(WheelSlotCount);
        var hasVoice = new List<bool>(WheelSlotCount);
        for (var i = 0; i < WheelSlotCount; i++)
        {
            if (i < lines.Count)
            {
                texts.Add(lines[i].Text ?? "");
                hasVoice.Add(lines[i].Emotion != null);
            }
            else
            {
                texts.Add("");
                hasVoice.Add(false);
            }
        }
        return (texts, hasVoice);
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
        var dllPath = typeof(Plugin).Assembly.Location;
        return Path.GetDirectoryName(dllPath) ?? AppContext.BaseDirectory;
    }
}

// IClock backed by Godot's monotonic timer.
file sealed class GodotClock : IClock
{
    public double NowSeconds() => Time.GetTicksMsec() / 1000.0;
}
