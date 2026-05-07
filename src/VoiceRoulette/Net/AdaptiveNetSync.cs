using System;
using Godot;
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

    private void TryRefreshBus()
    {
        var netService = RunManager.Instance?.NetService;
        var connected = netService is { IsConnected: true };

        if (connected && _bus == null)
        {
            try
            {
                _bus = new Sts2BusNetSync(netService!);
                _bus.LineReceived += OnAnyLineReceived;
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
            GD.Print("[VR][Net] Co-op session ended — reverted to LocalNetSync");
        }
    }

    private void DisposeBus()
    {
        if (_bus == null) return;
        _bus.LineReceived -= OnAnyLineReceived;
        try { _bus.Dispose(); } catch { }
        _bus = null;
    }

    private void OnAnyLineReceived(WireMessage msg) => LineReceived?.Invoke(msg);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_tree != null && _processHandler != null)
            _tree.ProcessFrame -= _processHandler;
        DisposeBus();
    }
}
