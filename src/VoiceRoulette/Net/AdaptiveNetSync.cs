using System;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace VoiceRoulette.Net;

/// <summary>
/// Adaptive transport: routes through STS2's typed message bus when a co-op
/// session is live, otherwise falls back to local-only echo. Re-checks the
/// session state on every Broadcast and once per second in the background, so
/// messages start broadcasting the moment a player joins or hosts co-op
/// (without restarting the mod).
/// </summary>
public sealed class AdaptiveNetSync : INetSync, IDisposable
{
    public event Action<WireMessage>? LineReceived;
    public event Action<MarkerWire>? MarkerReceived;

    private readonly LocalNetSync _local = new();
    private Sts2BusNetSync? _bus;
    private SceneTree? _tree;
    private Action? _processHandler;
    private double _nextCheckSec;
    private double _nextHeartbeatSec;
    private bool _disposed;

    public AdaptiveNetSync()
    {
        _local.LineReceived += OnAnyLineReceived;
        _local.MarkerReceived += OnAnyMarkerReceived;

        _tree = (SceneTree)Engine.GetMainLoop();
        _processHandler = OnProcessFrame;
        _tree.ProcessFrame += _processHandler;
        GD.Print("[VR][Net] AdaptiveNetSync started (will auto-detect co-op session)");
    }

    public void Broadcast(WireMessage msg)
    {
        if (_disposed) return;
        TryRefreshBus();
        if (_bus != null)
        {
            try { _bus.Broadcast(msg); return; }
            catch (Exception ex)
            {
                GD.PrintErr($"[VR][Net] bus broadcast failed: {ex.GetType().Name}: {ex.Message}");
                DisposeBus();
            }
        }
        GD.Print($"[VR][Net] LOCAL broadcast (no co-op bus available): sender={msg.Sender} text='{msg.Text}'");
        _local.Broadcast(msg);
    }

    public void BroadcastMarker(MarkerWire marker)
    {
        if (_disposed) return;
        TryRefreshBus();
        if (_bus != null)
        {
            try { _bus.BroadcastMarker(marker); return; }
            catch (Exception ex)
            {
                GD.PrintErr($"[VR][Net] bus marker broadcast failed: {ex.GetType().Name}: {ex.Message}");
                DisposeBus();
            }
        }
        _local.BroadcastMarker(marker);
    }

    private void OnProcessFrame()
    {
        if (_disposed) return;
        var nowSec = Time.GetTicksMsec() / 1000.0;
        if (nowSec < _nextCheckSec) return;
        _nextCheckSec = nowSec + 1.0;
        TryRefreshBus();

        // Emit a heartbeat every 15s so the user can confirm the polling is
        // alive and see the current transport state in the log.
        if (nowSec >= _nextHeartbeatSec)
        {
            _nextHeartbeatSec = nowSec + 15.0;
            var ns = RunManager.Instance?.NetService;
            GD.Print($"[VR][Net] heartbeat: bus={(_bus != null ? "active" : "inactive")} netService={(ns == null ? "null" : (ns.IsConnected ? "connected" : "not connected"))}");
        }
    }

    // Track the NetService instance we attached to. STS2 may swap NetService
    // (lobby → in-game), and our handler stays on the old one — so packets
    // routed through the new instance vanish silently. When the instance
    // changes, recreate the bus on the new one.
    private INetGameService? _attachedNetService;

    private void TryRefreshBus()
    {
        var netService = RunManager.Instance?.NetService;
        var connected = netService is { IsConnected: true };

        // Detect NetService swap — if the live instance is no longer the one
        // our bus is attached to, dispose and re-attach to the live instance.
        if (_bus != null && netService != null && !ReferenceEquals(netService, _attachedNetService))
        {
            GD.Print($"[VR][Net] NetService instance changed (old NetId={_attachedNetService?.NetId.ToString() ?? "?"}, new NetId={netService.NetId}) — re-attaching bus");
            DisposeBus();
            // fall through to recreation below
        }

        if (connected && _bus == null)
        {
            try
            {
                _bus = new Sts2BusNetSync(netService!);
                _bus.LineReceived += OnAnyLineReceived;
                _bus.MarkerReceived += OnAnyMarkerReceived;
                _attachedNetService = netService;
                var localPid = PlayerSlotResolver.ResolveLocalPlayerId();
                var localSlot = PlayerSlotResolver.ResolveLocalSlot();
                GD.Print($"[VR][Net] Co-op session detected — switched to Sts2BusNetSync (localPlayerId={(localPid?.ToString() ?? "unknown")} localSlot={localSlot})");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[VR][Net] failed to create bus sync: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
        else if (!connected && _bus != null)
        {
            DisposeBus();
            _attachedNetService = null;
            PlayerSlotResolver.Reset();
            GD.Print("[VR][Net] Co-op session ended — reverted to LocalNetSync");
        }
    }

    private void DisposeBus()
    {
        if (_bus == null) return;
        _bus.LineReceived -= OnAnyLineReceived;
        _bus.MarkerReceived -= OnAnyMarkerReceived;
        try { _bus.Dispose(); } catch { }
        _bus = null;
    }

    private void OnAnyLineReceived(WireMessage msg) => LineReceived?.Invoke(msg);
    private void OnAnyMarkerReceived(MarkerWire m) => MarkerReceived?.Invoke(m);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_tree != null && _processHandler != null)
            _tree.ProcessFrame -= _processHandler;
        DisposeBus();
    }
}
