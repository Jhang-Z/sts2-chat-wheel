using System;

namespace VoiceRoulette.Net;

/// <summary>
/// In-process loopback NetSync — used when no multiplayer session is active,
/// or as a development stub. Broadcasts deliver synchronously to LineReceived.
/// (Self-echo is filtered by Dispatcher.OnReceived using the sender slot.)
/// </summary>
public sealed class LocalNetSync : INetSync
{
    public event Action<WireMessage>? LineReceived;
    public event Action<MarkerWire>? MarkerReceived;

    public void Broadcast(WireMessage msg) => LineReceived?.Invoke(msg);
    public void BroadcastMarker(MarkerWire marker) => MarkerReceived?.Invoke(marker);
}
