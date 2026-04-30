// CONFIRMED mod entry-point (from binary analysis of sts2.dll, 2026-05-01):
//
// ModInitializerAttribute exists in MegaCrit.Sts2.Core.Modding namespace.
// ModManager.CallModInitializer scans loaded mod assemblies for static methods
// decorated with [ModInitializer] and invokes them during mod loading.
//
// Usage: apply [ModInitializer] to a public static void method (no parameters).
// ModManager discovers it by reflection — no interface or base class required.
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

public static class Plugin
{
    public const string Id = "voice_roulette";
    public const string Version = "0.1.0";

    private static readonly string ModDir = ResolveModDir();

    // [ModInitializer] — called by MegaCrit.Sts2.Core.Modding.ModManager.CallModInitializer
    // when the mod assembly is loaded. Must be public static void with no parameters.
    public static void Initialize()
    {
        try
        {
            var sceneRoot = ((SceneTree)Engine.GetMainLoop()).Root;

            var configPath = Path.Combine(ModDir, "config.json");
            var config = ConfigStore.Load(configPath);
            config.Save(); // write defaults if file was missing

            var registry = new LineRegistry(config.Schema);

            var cacheDir = Path.Combine(ModDir, "cache");
            const long CacheMaxBytes = 100 * 1024 * 1024;
            var cache = new AudioCache(cacheDir, CacheMaxBytes);

            var doubao = new DoubaoClient(
                config.Schema.Doubao.Endpoint,
                config.Schema.Doubao.ApiKey);
            var tts = new TTSPipeline(doubao, cache);

            var wheel = new WheelUI();
            sceneRoot.AddChild(wheel);

            var audio = new AudioPlayer(tts);
            sceneRoot.AddChild(audio);

            // Prefer real STS2 net bus; fall back to local loopback for singleplayer.
            INetSync net = Sts2BusNetSync.TryCreate() ?? (INetSync)new LocalNetSync();

            // v0.1 stubs: localSlot and character not derivable without confirmed API.
            const byte LocalSlot = 0;
            const string Character = "ironclad";

            var dispatcher = new Dispatch.Dispatcher(
                localSlot: LocalSlot,
                new Cooldown(config.Schema.Cooldown.PerSend, config.Schema.Cooldown.WindowMax),
                net, audio, new GodotClock());

            var input = new InputCapture(
                Key.V, wheel,
                getLineTexts: () => CurrentPageTexts(registry, Character));
            sceneRoot.AddChild(input);

            input.Released += idx =>
            {
                if (idx < 0) return;
                var pageSize = config.Schema.Pages.Common.Count;
                if (idx >= pageSize) return;
                var line = registry.Resolve(WheelPage.Common, idx, Character);
                dispatcher.Send(line);
            };
        }
        catch (Exception ex)
        {
            // Surface to Godot's error log rather than silently swallowing.
            GD.PrintErr($"[VoiceRoulette] Initialize failed: {ex}");
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
