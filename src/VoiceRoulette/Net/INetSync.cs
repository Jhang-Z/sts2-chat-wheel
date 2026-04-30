using System;

namespace VoiceRoulette.Net;

public interface INetSync
{
    void Broadcast(WireMessage msg);
    event Action<WireMessage>? LineReceived;
}
