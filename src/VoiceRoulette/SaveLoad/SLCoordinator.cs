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

        StartLocalProposal(localId, ts, isLocalProposer: true);

        if (ns != null)
        {
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
        else
        {
            GD.Print("[VR][SL] singleplayer — local-only proposal");
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

    /// <summary>User pressed Y (accept) — just dismisses local prompt; execution waits for timer.</summary>
    public void AcceptLocally()
    {
        if (_active == null) return;
        // Y just hides the prompt — execution still waits for the full vote window
        // so a late NO from someone else can still cancel.
        _ui?.Hide();
        GD.Print("[VR][SL] local accept — prompt dismissed, waiting for vote window");
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
        GD.Print("[VR][SL] vote window passed — executing SL");
        _active = null;
        _ui?.HideWithMessage("载入存档点…", 2.0);

        // Defensive: prevent CleanUp from flushing a save over our pre-combat
        // autosave. RunManager.ShouldSave has a setter — flip it via reflection
        // since we can't statically reference the property type without
        // pulling more game internals.
        TrySetShouldSaveFalse();

        // Trigger return-to-main-menu. In MP this also tears down NetService
        // via CleanUp, which propagates a disconnect to all peers that haven't
        // already started their own teardown.
        try
        {
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            if (nGameType == null) { GD.PrintErr("[VR][SL] NGame type not loadable"); return; }
            var instance = nGameType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance == null) { GD.PrintErr("[VR][SL] NGame.Instance is null"); return; }
            var method = nGameType.GetMethod("ReturnToMainMenuAfterRun", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) { GD.PrintErr("[VR][SL] NGame.ReturnToMainMenuAfterRun not found"); return; }
            method.Invoke(instance, null);  // returns Task; we don't await
            GD.Print("[VR][SL] NGame.ReturnToMainMenuAfterRun() invoked");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] execute failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TrySetShouldSaveFalse()
    {
        try
        {
            var rmType = Type.GetType("MegaCrit.Sts2.Core.Runs.RunManager, sts2");
            if (rmType == null) return;
            var instance = rmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance == null) return;
            var prop = rmType.GetProperty("ShouldSave", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.SetMethod == null) { GD.Print("[VR][SL] ShouldSave setter not found — skipping"); return; }
            prop.SetValue(instance, false);
            GD.Print("[VR][SL] RunManager.ShouldSave forced to false");
        }
        catch (Exception ex)
        {
            GD.Print($"[VR][SL] could not flip ShouldSave (non-fatal): {ex.Message}");
        }
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
