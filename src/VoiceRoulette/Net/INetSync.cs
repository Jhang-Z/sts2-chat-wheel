using System;

namespace VoiceRoulette.Net;

public interface INetSync
{
    void Broadcast(WireMessage msg);
    void BroadcastMarker(MarkerWire marker);
    event Action<WireMessage>? LineReceived;
    event Action<MarkerWire>? MarkerReceived;
}
