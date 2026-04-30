using System.Collections.Generic;

namespace VoiceRoulette.Dispatch;

public sealed class Cooldown
{
    private readonly double _perSend;
    private readonly int _windowMax;
    private readonly double _windowSeconds;
    private readonly Dictionary<byte, Queue<double>> _history = new();

    public Cooldown(double perSendSeconds, int windowMax, double windowSeconds = 60.0)
    {
        _perSend = perSendSeconds;
        _windowMax = windowMax;
        _windowSeconds = windowSeconds;
    }

    public bool TryRecord(byte playerId, double nowSeconds)
    {
        if (!_history.TryGetValue(playerId, out var q))
        {
            q = new Queue<double>();
            _history[playerId] = q;
        }

        // Evict aged entries.
        while (q.Count > 0 && nowSeconds - q.Peek() >= _windowSeconds)
            q.Dequeue();

        // Per-send check.
        if (q.Count > 0 && nowSeconds - LastOf(q) < _perSend) return false;

        // Window cap.
        if (q.Count >= _windowMax) return false;

        q.Enqueue(nowSeconds);
        return true;
    }

    private static double LastOf(Queue<double> q)
    {
        double last = 0;
        foreach (var v in q) last = v;
        return last;
    }
}
