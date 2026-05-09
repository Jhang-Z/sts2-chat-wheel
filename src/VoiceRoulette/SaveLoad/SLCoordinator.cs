// Multiplayer SL (Save & Load) coordinator — V1.5.
//
// Protocol (peer-to-peer, no host special role):
//   1. Anyone presses Cmd/Ctrl+Shift+R → proposer broadcasts SLRequestMessage
//      and shows local prompt. Proposer is implicitly counted as accepted.
//   2. Every other peer receives SLRequest and shows the same prompt with
//      live "已确认 X / N" counter.
//   3. Pressing 立即执行 SL broadcasts SLVoteMessage{accept=true};
//      pressing 反对 broadcasts SLVoteMessage{accept=false}.
//   4. ANY accept=false from anyone → all peers cancel.
//   5. When |acceptedSet| == N (peer count from RunState.Players) on every
//      peer independently, all peers trigger Execute() in sync.
//   6. Execute = NGame.ReturnToMainMenuAfterRun(). After landing at main
//      menu, mod auto-rehosts (if host) or auto-rejoins (if Steam client),
//      then auto-readies. Game's own BeginRunForAllPlayersIfAllReady fires
//      naturally when all are ready.
//
// Singleplayer fast-path: hotkey → execute immediately, no prompt.
//
// Pre-disconnect capture: each peer caches their role (Host/Client) + host's
// Steam ID (for clients) BEFORE teardown, because RunManager.NetService gets
// nulled out by CleanUp.

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
    /// <summary>Hard timeout — even if some peer never responds, we cancel after this so the prompt can't get stuck forever.</summary>
    public const double VoteTimeoutSec = 30.0;

    private const int RequestFixedTypeId = 202;
    private const int VoteFixedTypeId    = 203;

    private SceneTree? _tree;
    private UI.SLPromptOverlay? _ui;

    private INetGameService? _attachedNetService;
    private MessageHandlerDelegate<SLRequestMessage>? _requestHandler;
    private MessageHandlerDelegate<SLVoteMessage>?    _voteHandler;

    private ActiveProposal? _active;

    private sealed class ActiveProposal
    {
        public ulong ProposerId;
        public ulong Timestamp;
        public double DeadlineSec;
        public HashSet<ulong> Accepted = new();
        public int ExpectedPeerCount;
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

    // ── Public entry: local hotkey ─────────────────────────────────────────

    public void RequestSLLocally()
    {
        if (_active != null)
        {
            GD.Print("[VR][SL] hotkey ignored — proposal already in flight");
            return;
        }

        var ts = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (IsEffectivelySinglePlayer())
        {
            GD.Print("[VR][SL] singleplayer — executing immediately");
            _active = new ActiveProposal { ProposerId = 0, Timestamp = ts, DeadlineSec = 0, ExpectedPeerCount = 1 };
            _ui?.HideWithMessage("载入存档点…", 2.0);
            Execute();
            return;
        }

        var ns = TryGetNetService();
        if (ns == null)
        {
            GD.PrintErr("[VR][SL] hotkey: cannot determine NetService — abort");
            return;
        }
        var localId = ns.NetId;
        var peerCount = ResolvePeerCount();
        StartLocalProposal(localId, ts, isLocalProposer: true, peerCount);

        try
        {
            ns.SendMessage(new SLRequestMessage(localId, ts));
            GD.Print($"[VR][SL] broadcast SLRequest proposer={localId} ts={ts} expectedPeers={peerCount}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][SL] broadcast failed: {ex.Message}");
        }
    }

    public void VetoLocally()
    {
        if (_active == null) return;
        var proposalTs = _active.Timestamp;
        var ns = TryGetNetService();
        var voterId = ns?.NetId ?? 0UL;

        CancelLocal("local veto");
        if (ns != null)
        {
            try { ns.SendMessage(new SLVoteMessage(voterId, proposalTs, accept: false)); }
            catch (Exception ex) { GD.PrintErr($"[VR][SL] veto broadcast failed: {ex.Message}"); }
        }
    }

    public void AcceptLocally()
    {
        if (_active == null) return;
        var ns = TryGetNetService();
        var localId = ns?.NetId ?? 0UL;

        // Mark self accepted locally (idempotent).
        if (_active.Accepted.Add(localId))
            UpdateUiCounter();

        if (ns != null)
        {
            try
            {
                ns.SendMessage(new SLVoteMessage(localId, _active.Timestamp, accept: true));
                GD.Print($"[VR][SL] broadcast SLVote(YES) proposalTs={_active.Timestamp}");
            }
            catch (Exception ex) { GD.PrintErr($"[VR][SL] accept broadcast failed: {ex.Message}"); }
        }

        CheckAllAccepted();
    }

    // ── Internal state ─────────────────────────────────────────────────────

    private void StartLocalProposal(ulong proposerId, ulong timestamp, bool isLocalProposer, int peerCount)
    {
        var nowSec = Time.GetTicksMsec() / 1000.0;
        _active = new ActiveProposal
        {
            ProposerId = proposerId,
            Timestamp = timestamp,
            DeadlineSec = nowSec + VoteTimeoutSec,
            ExpectedPeerCount = Math.Max(1, peerCount),
        };
        // Proposer NOT auto-accepted — every peer (including proposer) must
        // explicitly click Confirm. Counter starts at 0/N for everyone.
        var localId = TryGetNetService()?.NetId ?? 0UL;
        var byMe = isLocalProposer || proposerId == localId;
        _ui?.Show(byMe, _active.ExpectedPeerCount, _active.Accepted.Count);
    }

    private void UpdateUiCounter()
    {
        if (_active == null || _ui == null) return;
        _ui.SetCounter(_active.Accepted.Count, _active.ExpectedPeerCount);
    }

    private void CheckAllAccepted()
    {
        if (_active == null) return;
        if (_active.Accepted.Count < _active.ExpectedPeerCount) return;
        GD.Print($"[VR][SL] all {_active.ExpectedPeerCount} peer(s) accepted — executing");
        _ui?.HideWithMessage("载入存档点…", 2.0);
        Execute();
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

        // Capture pre-disconnect state — RunManager.NetService gets cleared
        // during CleanUp. We need role + host SteamID for auto-rejoin.
        var ctx = SLPostMenu.CapturePreDisconnect();
        GD.Print($"[VR][SL] captured role={ctx.Role} platform={ctx.Platform} hostSteamId={ctx.HostSteamId}");

        // CRITICAL: only Host and SP actively trigger ReturnToMainMenuAfterRun.
        // Clients ride the natural "host disconnected" flow — when host's
        // teardown disconnects them, the game's own handler returns them
        // to main menu, where our auto-rejoin chain takes over.
        //
        // If client also called ReturnToMainMenuAfterRun, it would race the
        // network-disconnect handler and one of the two teardown paths
        // would clobber the auto-rejoin scheduling.
        Task? teardownTask = null;
        if (ctx.Role != SLRole.Client)
        {
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
        }
        else
        {
            GD.Print("[VR][SL] client: skipping voluntary teardown — will ride host's disconnect");
        }

        _ = SLPostMenu.AfterMenuChainAsync(teardownTask, ctx);
    }

    private void OnTick()
    {
        TryAttachToNetService();
        if (_active == null) return;
        var nowSec = Time.GetTicksMsec() / 1000.0;
        if (nowSec >= _active.DeadlineSec) CancelLocal("timeout");
    }

    // ── Net wiring ─────────────────────────────────────────────────────────

    private INetGameService? TryGetNetService()
    {
        var ns = RunManager.Instance?.NetService;
        return ns is { IsConnected: true } ? ns : null;
    }

    private static bool IsEffectivelySinglePlayer()
    {
        var rm = RunManager.Instance;
        if (rm == null) return true;
        try
        {
            var prop = rm.GetType().GetProperty("IsSinglePlayerOrFakeMultiplayer", BindingFlags.Public | BindingFlags.Instance);
            if (prop?.GetValue(rm) is bool b) return b;
        }
        catch (Exception ex) { GD.Print($"[VR][SL] IsSinglePlayerOrFakeMultiplayer probe failed: {ex.Message}"); }
        var ns = rm.NetService;
        if (ns == null) return true;
        try
        {
            var typeProp = ns.GetType().GetProperty("Type");
            return typeProp?.GetValue(ns)?.ToString() == "Singleplayer";
        }
        catch { return false; }
    }

    /// <summary>
    /// Total peer count for vote tally. Pulled from RunState.Players, which
    /// is mirrored on every peer (host and clients).
    /// </summary>
    private static int ResolvePeerCount()
    {
        try
        {
            var rm = RunManager.Instance;
            if (rm == null) return 1;
            var stateProp = rm.GetType().GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateProp?.GetValue(rm);
            var playersProp = state?.GetType().GetProperty("Players");
            if (playersProp?.GetValue(state) is System.Collections.IEnumerable players)
            {
                var count = 0;
                foreach (var _ in players) count++;
                return Math.Max(1, count);
            }
        }
        catch (Exception ex) { GD.Print($"[VR][SL] peer count fallback: {ex.Message}"); }
        return 1;
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
        if (_active != null && _active.Timestamp == msg.Timestamp) return;  // self-echo
        if (_active != null)
        {
            GD.Print("[VR][SL] new proposal arrived while one is in flight — ignoring");
            return;
        }
        StartLocalProposal(msg.ProposerId, msg.Timestamp, isLocalProposer: false, ResolvePeerCount());
    }

    private void HandleSLVote(SLVoteMessage msg, ulong senderId)
    {
        if (_active == null || msg.ProposalTimestamp != _active.Timestamp) return;
        if (!msg.Accept)
        {
            GD.Print($"[VR][SL] received NO vote from {msg.VoterId} — cancelling");
            CancelLocal($"veto from {msg.VoterId}");
            return;
        }
        if (_active.Accepted.Add(msg.VoterId))
        {
            GD.Print($"[VR][SL] +1 accept from {msg.VoterId} (now {_active.Accepted.Count}/{_active.ExpectedPeerCount})");
            UpdateUiCounter();
            CheckAllAccepted();
        }
    }

    // ── Reflective injection (mirrors Sts2BusNetSync) ─────────────────────

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
