// Multiplayer SL (Save & Load) coordinator.
//
// Protocol (peer-to-peer, no host special role, no roster required):
//   1. Anyone presses Cmd/Ctrl+Shift+R → proposer broadcasts SLRequestMessage
//      and shows local prompt.
//   2. Every other peer receives SLRequest and shows the same prompt
//      ("队友 X 请求 SL — Y 同意 / N 反对").
//   3. Pressing N broadcasts SLVoteMessage{accept=false}. Any peer (including
//      proposer) receiving a NO vote cancels its local execution timer.
//   4. After 10s with no NO seen → every peer executes
//      NGame.ReturnToMainMenuAfterRun(). Disconnect cascade triggers
//      RunManager.CleanUp on each peer; the auto-saved RunState (from the
//      last room boundary) stays untouched, so on Continue everyone resumes
//      from before the bad combat/event.
//
// Singleplayer: no broadcast. Hotkey → local prompt → Y executes, N cancels.
// (Detected by NetService == null or not connected.)
//
// Why we DON'T re-save before quitting: SL's whole point is to keep the
// previous autosave. If RunManager.CleanUp(graceful=true) tries to flush a
// fresh save, we'd overwrite the SL target. Reflection sets ShouldSave=false
// before triggering the menu return, defending against that path.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace VoiceRoulette.SaveLoad;

public sealed partial class SLCoordinator : Node
{
    public const double VoteWindowSec = 10.0;

    // Fixed type IDs for SL messages — must match across peers. Picked
    // contiguous to existing 200/201 (voice/marker).
    private const int RequestFixedTypeId = 202;
    private const int VoteFixedTypeId    = 203;

    private SceneTree? _tree;
    private UI.SLPromptOverlay? _ui;

    private INetGameService? _attachedNetService;
    private MessageHandlerDelegate<SLRequestMessage>? _requestHandler;
    private MessageHandlerDelegate<SLVoteMessage>?    _voteHandler;

    // Active proposal state — null means no proposal in flight.
    private ActiveProposal? _active;

    private sealed class ActiveProposal
    {
        public ulong ProposerId;
        public ulong Timestamp;
        public double DeadlineSec;
        public bool Vetoed;
    }

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][SL] SLCoordinator started");
    }

    public void AttachUi(UI.SLPromptOverlay ui) => _ui = ui;

    public bool IsPromptActive => _active != null;

    public override void _ExitTree()
    {
        if (_tree != null) _tree.ProcessFrame -= OnTick;
        DetachFromNetService();
    }

    // ── Public entry: local hotkey pressed ─────────────────────────────────

    public void RequestSLLocally()
    {
        if (_active != null)
        {
            GD.Print("[VR][SL] hotkey ignored — proposal already in flight");
            return;
        }

        var ns = TryGetNetService();
        var localId = ns?.NetId ?? 0UL;
        var ts = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Singleplayer fast-path: no peers to coordinate with, no veto window
        // makes sense. Just execute. We mark _active so subsequent hotkey
        // presses during the teardown don't queue a second SL.
        if (ns == null)
        {
            GD.Print("[VR][SL] singleplayer — executing immediately");
            _active = new ActiveProposal { ProposerId = 0, Timestamp = ts, DeadlineSec = 0, Vetoed = false };
            _ui?.HideWithMessage("载入存档点…", 2.0);
            Execute();
            return;
        }

        StartLocalProposal(localId, ts, isLocalProposer: true);
        try
        {
            ns.SendMessage(new SLRequestMessage(localId, ts));
            GD.Print($"[VR][SL] broadcast SLRequest proposer={localId} ts={ts}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] broadcast failed: {ex.Message}");
        }
    }

    /// <summary>User pressed N (veto) on the prompt UI.</summary>
    public void VetoLocally()
    {
        if (_active == null) return;
        var proposalTs = _active.Timestamp;
        var ns = TryGetNetService();
        var voterId = ns?.NetId ?? 0UL;

        // Veto cancels locally first, then propagates.
        CancelLocal("local veto");
        if (ns != null)
        {
            try
            {
                ns.SendMessage(new SLVoteMessage(voterId, proposalTs, accept: false));
                GD.Print($"[VR][SL] broadcast SLVote(NO) proposalTs={proposalTs}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[VR][SL] veto broadcast failed: {ex.Message}");
            }
        }
    }

    /// <summary>User pressed Confirm — only the proposer can fast-forward. Non-proposers
    /// just dismiss their local prompt cosmetically; the actual execution still waits
    /// for the timer so a late veto from anyone is still respected.</summary>
    public void AcceptLocally()
    {
        if (_active == null) return;
        var ns = TryGetNetService();
        var localId = ns?.NetId ?? 0UL;
        var iAmProposer = _active.ProposerId == localId || _active.ProposerId == 0UL;
        if (iAmProposer)
        {
            GD.Print("[VR][SL] proposer fast-forwards — executing now (host disconnect propagates to clients)");
            _ui?.HideWithMessage("载入存档点…", 2.0);
            Execute();
        }
        else
        {
            _ui?.Hide();
            GD.Print("[VR][SL] non-proposer accept — prompt dismissed, waiting for proposer or timer");
        }
    }

    // ── Internal state machine ─────────────────────────────────────────────

    private void StartLocalProposal(ulong proposerId, ulong timestamp, bool isLocalProposer)
    {
        var nowSec = Time.GetTicksMsec() / 1000.0;
        _active = new ActiveProposal
        {
            ProposerId = proposerId,
            Timestamp = timestamp,
            DeadlineSec = nowSec + VoteWindowSec,
            Vetoed = false,
        };

        var localId = TryGetNetService()?.NetId ?? 0UL;
        var byMe = isLocalProposer || proposerId == localId || proposerId == 0UL;
        _ui?.Show(byMe, VoteWindowSec);
    }

    private void CancelLocal(string reason)
    {
        if (_active == null) return;
        GD.Print($"[VR][SL] proposal cancelled: {reason}");
        _active = null;
        _ui?.HideWithMessage("已取消", 1.5);
    }

    private void Execute()
    {
        if (_active == null) return;
        GD.Print("[VR][SL] executing SL");
        _active = null;

        // Decide whether to auto-resume from the on-disk save. SP yes, MP no
        // (V1.0 doesn't auto-rejoin lobby). Captured BEFORE teardown because
        // RunManager.NetService gets nulled out by CleanUp.
        var rm = RunManager.Instance;
        var ns = rm?.NetService;
        var wasSinglePlayer = ns == null || !ns.IsConnected;

        // The on-disk autosave is from when the previous room ended (game's
        // SaveRun is only called from RunManager.OnEnded). So we don't need
        // to suppress saving here — the desired save is already on disk and
        // CleanUp doesn't write a new one. Just tear down + bounce to menu.
        Task? teardownTask = null;
        try
        {
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            var instance = nGameType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var method = nGameType?.GetMethod("ReturnToMainMenuAfterRun", BindingFlags.Public | BindingFlags.Instance);
            if (instance == null || method == null)
            {
                GD.PrintErr("[VR][SL] NGame.ReturnToMainMenuAfterRun not resolvable");
                return;
            }
            teardownTask = method.Invoke(instance, null) as Task;
            GD.Print("[VR][SL] NGame.ReturnToMainMenuAfterRun() invoked");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] execute failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (wasSinglePlayer)
        {
            _ = ChainAutoContinueAsync(teardownTask);
        }
    }

    /// <summary>
    /// After the return-to-main-menu Task completes, the main menu is fully
    /// constructed in the scene tree. Find NMainMenu and invoke its Continue
    /// handler — the same handler the user would otherwise click manually.
    /// </summary>
    private static async Task ChainAutoContinueAsync(Task? teardownTask)
    {
        try
        {
            if (teardownTask != null) await teardownTask;
            GD.Print("[VR][SL] return-to-menu Task completed; auto-clicking Continue");

            // Walk scene tree to find NMainMenu instance.
            var tree = (SceneTree)Engine.GetMainLoop();
            var nMainMenuType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu, sts2")
                ?? throw new InvalidOperationException("NMainMenu type not loadable");
            Node? menu = null;
            for (var attempt = 0; attempt < 30 && menu == null; attempt++)
            {
                menu = FindDescendantOfType(tree.Root, nMainMenuType);
                if (menu == null) await Task.Delay(100);  // give scene tree a chance
            }
            if (menu == null)
            {
                GD.PrintErr("[VR][SL] auto-continue: NMainMenu not found in scene tree after 3s");
                return;
            }

            var method = nMainMenuType.GetMethod("OnContinueButtonPressedAsync", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                GD.PrintErr("[VR][SL] OnContinueButtonPressedAsync not found");
                return;
            }
            var contTask = method.Invoke(menu, null) as Task;
            GD.Print("[VR][SL] OnContinueButtonPressedAsync invoked");
            if (contTask != null) await contTask;
            GD.Print("[VR][SL] auto-continue completed");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] auto-continue failed: {ex.GetType().Name}: {ex.Message}");
        }
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

    private void OnTick()
    {
        if (_active == null) return;
        TryAttachToNetService();
        var nowSec = Time.GetTicksMsec() / 1000.0;
        if (nowSec >= _active.DeadlineSec) Execute();
    }

    // ── Net wiring (lazy attach, mirrors AdaptiveNetSync pattern) ──────────

    private INetGameService? TryGetNetService()
    {
        var ns = RunManager.Instance?.NetService;
        return ns is { IsConnected: true } ? ns : null;
    }

    private void TryAttachToNetService()
    {
        var ns = TryGetNetService();
        if (ns == null)
        {
            if (_attachedNetService != null) DetachFromNetService();
            return;
        }
        if (ReferenceEquals(ns, _attachedNetService)) return;

        // Fresh attach (or NetService swap mid-session).
        DetachFromNetService();
        try
        {
            InjectIntoMessageTypesCache(typeof(SLRequestMessage), RequestFixedTypeId);
            InjectIntoMessageTypesCache(typeof(SLVoteMessage),    VoteFixedTypeId);

            _requestHandler = HandleSLRequest;
            _voteHandler    = HandleSLVote;
            ns.RegisterMessageHandler(_requestHandler);
            ns.RegisterMessageHandler(_voteHandler);
            _attachedNetService = ns;
            GD.Print($"[VR][SL] attached to NetService NetId={ns.NetId}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] attach failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void DetachFromNetService()
    {
        if (_attachedNetService == null) return;
        try
        {
            if (_requestHandler != null) _attachedNetService.UnregisterMessageHandler(_requestHandler);
            if (_voteHandler    != null) _attachedNetService.UnregisterMessageHandler(_voteHandler);
        }
        catch { }
        _attachedNetService = null;
        _requestHandler = null;
        _voteHandler = null;
    }

    private void HandleSLRequest(SLRequestMessage msg, ulong senderId)
    {
        GD.Print($"[VR][SL] received SLRequest proposer={msg.ProposerId} ts={msg.Timestamp} from senderId={senderId}");
        if (_active != null && _active.Timestamp == msg.Timestamp) return;  // self-echo or dup
        if (_active != null)
        {
            GD.Print("[VR][SL] new proposal arrived while one is in flight — ignoring");
            return;
        }
        StartLocalProposal(msg.ProposerId, msg.Timestamp, isLocalProposer: false);
    }

    private void HandleSLVote(SLVoteMessage msg, ulong senderId)
    {
        if (_active == null || msg.ProposalTimestamp != _active.Timestamp) return;
        if (msg.Accept) return;  // YES is implicit; we only act on NO
        GD.Print($"[VR][SL] received NO vote from {msg.VoterId} — cancelling");
        CancelLocal($"veto from {msg.VoterId}");
    }

    // ── Reflection: inject SL message types into MessageTypes._cache ──────
    // Mirrors Sts2BusNetSync.InjectIntoMessageTypesCache. Duplicated rather
    // than refactored because the SL feature is otherwise self-contained;
    // future cleanup can extract a common helper.

    private static void InjectIntoMessageTypesCache(Type messageType, int fixedId)
    {
        var msgTypesType = Type.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes, sts2")
            ?? throw new InvalidOperationException("MessageTypes type not loadable");
        var cacheField = msgTypesType.GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MessageTypes._cache not found");
        var cache = cacheField.GetValue(null) ?? throw new InvalidOperationException("_cache null");
        var cacheType = cache.GetType();
        var typeToIdField = cacheType.GetField("_typeToId", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var idToTypeField = cacheType.GetField("_idToType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var typeToId = (Dictionary<Type, int>)typeToIdField.GetValue(cache)!;
        var idToType = (List<Type>)idToTypeField.GetValue(cache)!;

        if (typeToId.TryGetValue(messageType, out var existing))
        {
            typeToId.Remove(messageType);
            if (existing >= 0 && existing < idToType.Count && idToType[existing] == messageType)
                idToType[existing] = typeof(object);
        }
        while (idToType.Count < fixedId) idToType.Add(typeof(object));
        if (idToType.Count == fixedId) idToType.Add(messageType);
        else idToType[fixedId] = messageType;
        typeToId[messageType] = fixedId;
        GD.Print($"[VR][SL] injected {messageType.Name} at id={fixedId} (idToType.Count={idToType.Count})");
    }
}
