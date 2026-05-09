// Post-disconnect chain: after ReturnToMainMenuAfterRun lands at the main
// menu, this handles the auto-rehost (host) or auto-rejoin (Steam client)
// flow so the SL feels seamless. Each branch ends with the player landed
// in NMultiplayerLoadGameScreen with SetReady(true) — when all peers are
// ready, the game's own BeginRunForAllPlayersIfAllReady fires and the run
// resumes from the on-disk autosave.
//
// Steam-only — ENet auto-rejoin is not supported (no stable host endpoint).
// ENet sessions fall through to "stranded at main menu" mode.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace VoiceRoulette.SaveLoad;

internal enum SLRole { Singleplayer, Host, Client, Unknown }
internal enum SLPlatform { None = 0, Steam = 1 }

internal sealed class SLContext
{
    public SLRole Role;
    public SLPlatform Platform;
    public ulong HostSteamId;   // for Client only
    public ulong LocalNetId;
}

internal static class SLPostMenu
{
    public static SLContext CapturePreDisconnect()
    {
        var ctx = new SLContext { Role = SLRole.Unknown, Platform = SLPlatform.None };
        try
        {
            var rm = RunManager.Instance;
            if (rm == null) { ctx.Role = SLRole.Singleplayer; return ctx; }
            var ns = rm.NetService;
            if (ns == null) { ctx.Role = SLRole.Singleplayer; return ctx; }
            ctx.LocalNetId = ns.NetId;

            var typeProp = ns.GetType().GetProperty("Type");
            var nsType = typeProp?.GetValue(ns)?.ToString();
            ctx.Role = nsType switch
            {
                "Singleplayer" => SLRole.Singleplayer,
                "Host"         => SLRole.Host,
                "Client"       => SLRole.Client,
                _              => SLRole.Unknown,
            };

            var platProp = ns.GetType().GetProperty("Platform");
            var platVal = platProp?.GetValue(ns)?.ToString();
            ctx.Platform = platVal == "Steam" ? SLPlatform.Steam : SLPlatform.None;

            // For Steam clients, NetClientGameService.HostNetId is the host's
            // Steam ID — stable forever. We use it to find the host's NEW
            // lobby after they re-host.
            if (ctx.Role == SLRole.Client)
            {
                var hostProp = ns.GetType().GetProperty("HostNetId");
                if (hostProp?.GetValue(ns) is ulong h) ctx.HostSteamId = h;
            }
        }
        catch (Exception ex) { GD.PrintErr($"[VR][SL] capture failed: {ex.Message}"); }
        return ctx;
    }

    public static async Task AfterMenuChainAsync(Task? teardownTask, SLContext ctx)
    {
        try
        {
            if (teardownTask != null) await teardownTask;
            GD.Print($"[VR][SL] post-menu chain start — role={ctx.Role} (teardownTask={(teardownTask != null ? "awaited" : "skipped, riding host disconnect")})");

            // Clients have to wait longer because the natural host-disconnect
            // flow takes time: TCP timeout → CleanUp → scene transition →
            // possibly an error popup → finally NMainMenu. 15s covers most
            // cases. Host/SP are immediate (we just teardown'd).
            var menuWaitMs = ctx.Role == SLRole.Client ? 15000 : 5000;
            var nMainMenu = await WaitForMainMenuAsync(menuWaitMs);
            if (nMainMenu == null)
            {
                GD.PrintErr($"[VR][SL] NMainMenu not found within {menuWaitMs}ms");
                return;
            }
            GD.Print("[VR][SL] NMainMenu detected; clearing any modal popups before continuing");

            // The "host disconnected" error popup may be modal-blocking.
            // Clear it via NModalContainer.Clear() so subsequent UI invocations
            // (JoinGame chain, etc.) aren't intercepted.
            TryClearModalPopups();

            switch (ctx.Role)
            {
                case SLRole.Singleplayer:
                    await InvokeContinueAsync(nMainMenu);
                    break;

                case SLRole.Host when ctx.Platform == SLPlatform.Steam:
                    await HostRehostAsync(nMainMenu, ctx);
                    break;

                case SLRole.Client when ctx.Platform == SLPlatform.Steam:
                    await ClientRejoinAsync(nMainMenu, ctx);
                    break;

                default:
                    GD.Print($"[VR][SL] role={ctx.Role} platform={ctx.Platform} — auto-rejoin not supported, stopping at main menu");
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] AfterMenuChain failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ── Singleplayer continue ─────────────────────────────────────────────

    private static async Task InvokeContinueAsync(Node nMainMenu)
    {
        var method = nMainMenu.GetType().GetMethod("OnContinueButtonPressedAsync",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null) { GD.PrintErr("[VR][SL] OnContinueButtonPressedAsync missing"); return; }
        if (method.Invoke(nMainMenu, null) is Task t) await t;
        GD.Print("[VR][SL] SP auto-continue completed");
    }

    // ── Host: re-host with the on-disk save ───────────────────────────────

    private static async Task HostRehostAsync(Node nMainMenu, SLContext ctx)
    {
        GD.Print("[VR][SL] host: loading multiplayer save + re-hosting");

        // 1. Steam needs a beat after CleanUp to fully release the previous
        //    lobby/socket. If we re-host too fast, the new Steam lobby gets
        //    created in a half-broken state — the host UI shows the load
        //    lobby but clients get "connection timeout" trying to join.
        await Task.Delay(2500);

        // 2. Load the multiplayer save from disk (latest autosave from when
        //    the previous room ended).
        var saveDataResult = TryLoadMultiplayerSave(ctx.LocalNetId);
        if (saveDataResult is not { } saveData)
        {
            GD.PrintErr("[VR][SL] host: multiplayer save load failed — abort");
            return;
        }

        var submenuType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu, sts2");
        if (submenuType == null) { GD.PrintErr("[VR][SL] NMultiplayerSubmenu type not loadable"); return; }

        // Open multiplayer submenu first so its lifecycle runs.
        await OpenMultiplayerSubmenuAsync(nMainMenu);

        var submenu = await WaitForDescendantAsync(submenuType, timeoutMs: 4000);
        if (submenu == null) { GD.PrintErr("[VR][SL] NMultiplayerSubmenu not found"); return; }

        // 3. CRITICAL: use StartHostAsync (private async) instead of the
        //    public StartHost wrapper. StartHost is fire-and-forget — it
        //    schedules StartHostAsync but returns immediately, so we'd
        //    proceed to SetReady before the Steam lobby is actually bound.
        //    The async version awaits StartSteamHost, which only resolves
        //    after Steam confirms the lobby is created and accepting
        //    connections.
        var startHostAsync = submenuType.GetMethod("StartHostAsync",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (startHostAsync != null)
        {
            try
            {
                if (startHostAsync.Invoke(submenu, new object?[] { saveData }) is Task hostTask)
                {
                    GD.Print("[VR][SL] host: awaiting StartHostAsync (Steam lobby creation)…");
                    await hostTask;
                    GD.Print("[VR][SL] host: StartHostAsync completed — Steam lobby is bound");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[VR][SL] host: StartHostAsync threw {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }
        else
        {
            GD.PrintErr("[VR][SL] host: StartHostAsync not found, falling back to fire-and-forget StartHost");
            var startHost = submenuType.GetMethod("StartHost", BindingFlags.Public | BindingFlags.Instance);
            if (startHost == null) { GD.PrintErr("[VR][SL] StartHost not found either"); return; }
            startHost.Invoke(submenu, new object?[] { saveData });
        }

        // 4. Wait for NMultiplayerLoadGameScreen to appear, then SetReady(true).
        await ReadyUpInLoadLobbyAsync();
    }

    // ── Client: rejoin host's new Steam lobby ─────────────────────────────

    private static async Task ClientRejoinAsync(Node nMainMenu, SLContext ctx)
    {
        if (ctx.HostSteamId == 0)
        {
            GD.PrintErr("[VR][SL] client: HostSteamId not captured — abort");
            return;
        }
        GD.Print($"[VR][SL] client: will rejoin Steam friend {ctx.HostSteamId}");

        // Initial wait — host needs time to finish teardown + open new lobby
        // + Steam needs time to propagate the new "in lobby" state to the
        // friends list. Mac Steam clients are noticeably slower than Win, so
        // bump generously.
        await Task.Delay(4000);

        var initType = Type.GetType("MegaCrit.Sts2.Core.Multiplayer.Connection.SteamClientConnectionInitializer, sts2");
        if (initType == null) { GD.PrintErr("[VR][SL] SteamClientConnectionInitializer type not loadable"); return; }
        var fromPlayer = initType.GetMethod("FromPlayer", BindingFlags.Public | BindingFlags.Static);
        if (fromPlayer == null) { GD.PrintErr("[VR][SL] FromPlayer factory not found"); return; }

        var joinGame = nMainMenu.GetType().GetMethod("JoinGame",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (joinGame == null) { GD.PrintErr("[VR][SL] NMainMenu.JoinGame not found"); return; }

        var loadScreenType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NMultiplayerLoadGameScreen, sts2");
        if (loadScreenType == null) { GD.PrintErr("[VR][SL] NMultiplayerLoadGameScreen type not loadable"); return; }

        const int MaxAttempts = 6;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // CRITICAL: JoinGameAsync does NOT throw on connection failure —
            // it catches internally, shows an NErrorPopup ("连接超时"), and
            // calls Disconnect. So awaiting the task always completes.
            // The reliable success signal is whether NMultiplayerLoadGameScreen
            // ends up on screen.
            //
            // Between attempts we clear any leftover error popup so the next
            // FromPlayer/JoinGame call isn't blocked by a modal.
            TryClearModalPopups();

            try
            {
                var initializer = fromPlayer.Invoke(null, new object?[] { ctx.HostSteamId });
                GD.Print($"[VR][SL] client: connection attempt {attempt}/{MaxAttempts} via Steam friend {ctx.HostSteamId}");
                if (joinGame.Invoke(nMainMenu, new[] { initializer }) is Task joinTask)
                    await joinTask;
                GD.Print("[VR][SL] client: JoinGame returned — checking if landed in load lobby…");
            }
            catch (Exception ex)
            {
                GD.Print($"[VR][SL] client: attempt {attempt} threw {ex.GetType().Name}: {ex.Message}");
            }

            // Success = NMultiplayerLoadGameScreen present in scene tree.
            // Give it up to 2s to appear (it's pushed inside JoinGameAsync but
            // submenu push animation may delay scene-tree visibility).
            var loadScreen = await WaitForDescendantAsync(loadScreenType, timeoutMs: 2000);
            if (loadScreen != null)
            {
                GD.Print($"[VR][SL] client: landed in load lobby on attempt {attempt}");
                await ReadyUpInLoadLobbyAsync();
                return;
            }

            GD.Print($"[VR][SL] client: attempt {attempt} did not connect (popup shown by game) — backing off");
            if (attempt < MaxAttempts) await Task.Delay(3500);
        }

        GD.PrintErr($"[VR][SL] client: all {MaxAttempts} rejoin attempts failed — please reconnect manually via 多人模式 → 加入");
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static object? TryLoadMultiplayerSave(ulong localNetId)
    {
        try
        {
            var smType = Type.GetType("MegaCrit.Sts2.Core.Saves.SaveManager, sts2");
            var sm = smType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var loadMethod = smType?.GetMethod("LoadAndCanonicalizeMultiplayerRunSave", BindingFlags.Public | BindingFlags.Instance);
            if (sm == null || loadMethod == null) { GD.PrintErr("[VR][SL] LoadAndCanonicalizeMultiplayerRunSave missing"); return null; }
            var result = loadMethod.Invoke(sm, new object?[] { localNetId });
            if (result == null) return null;
            var rt = result.GetType();
            var success = (bool)(rt.GetProperty("Success")?.GetValue(result) ?? false);
            if (!success)
            {
                var err = rt.GetProperty("ErrorMessage")?.GetValue(result)?.ToString() ?? "(no message)";
                GD.PrintErr($"[VR][SL] save read not successful: {err}");
                return null;
            }
            return rt.GetProperty("SaveData")?.GetValue(result);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] LoadAndCanonicalize threw: {ex.Message}");
            return null;
        }
    }

    private static void TryClearModalPopups()
    {
        try
        {
            var mcType = Type.GetType("MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer, sts2");
            var instance = mcType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var clear = mcType?.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            if (instance == null || clear == null)
            {
                GD.Print("[VR][SL] NModalContainer.Clear unavailable — skipping");
                return;
            }
            clear.Invoke(instance, null);
            GD.Print("[VR][SL] cleared modal popups");
        }
        catch (Exception ex)
        {
            GD.Print($"[VR][SL] TryClearModalPopups: {ex.Message} (non-fatal)");
        }
    }

    private static async Task<Node?> WaitForMainMenuAsync(int timeoutMs)
    {
        var nMainMenuType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu, sts2");
        if (nMainMenuType == null) return null;
        return await WaitForDescendantAsync(nMainMenuType, timeoutMs);
    }

    private static async Task<Node?> WaitForDescendantAsync(Type type, int timeoutMs)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var deadline = Time.GetTicksMsec() + (ulong)timeoutMs;
        while (Time.GetTicksMsec() < deadline)
        {
            var hit = FindDescendantOfType(tree.Root, type);
            if (hit != null) return hit;
            await Task.Delay(100);
        }
        return null;
    }

    private static Node? FindDescendantOfType(Node n, Type t)
    {
        if (t.IsInstanceOfType(n)) return n;
        foreach (var c in n.GetChildren())
        {
            var hit = FindDescendantOfType(c, t);
            if (hit != null) return hit;
        }
        return null;
    }

    private static async Task OpenMultiplayerSubmenuAsync(Node nMainMenu)
    {
        var open = nMainMenu.GetType().GetMethod("OpenMultiplayerSubmenu",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, Type.EmptyTypes, null);
        if (open == null) { GD.Print("[VR][SL] OpenMultiplayerSubmenu(no-args) not found, skipping"); return; }
        open.Invoke(nMainMenu, null);
        await Task.Delay(300);  // let the submenu push animation settle
    }

    /// <summary>
    /// After landing in NMultiplayerLoadGameScreen, programmatically set
    /// the lobby ready so the host's BeginRunForAllPlayersIfAllReady fires
    /// when everyone's ready.
    /// </summary>
    private static async Task ReadyUpInLoadLobbyAsync()
    {
        var screenType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NMultiplayerLoadGameScreen, sts2");
        if (screenType == null) { GD.PrintErr("[VR][SL] NMultiplayerLoadGameScreen type not loadable"); return; }
        var screen = await WaitForDescendantAsync(screenType, timeoutMs: 8000);
        if (screen == null) { GD.PrintErr("[VR][SL] NMultiplayerLoadGameScreen never appeared — manual ready required"); return; }

        // Pull the LoadRunLobby off the screen and call SetReady(true).
        var lobbyField = screenType.GetField("_runLobby", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var lobby = lobbyField?.GetValue(screen);
        if (lobby == null) { GD.PrintErr("[VR][SL] LoadRunLobby field not accessible"); return; }
        var setReady = lobby.GetType().GetMethod("SetReady", BindingFlags.Public | BindingFlags.Instance);
        if (setReady == null) { GD.PrintErr("[VR][SL] LoadRunLobby.SetReady not found"); return; }
        setReady.Invoke(lobby, new object?[] { true });
        GD.Print("[VR][SL] auto-ready set on LoadRunLobby");
    }
}
