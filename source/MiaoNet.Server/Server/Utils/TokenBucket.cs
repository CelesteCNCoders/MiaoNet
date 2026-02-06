using System.Diagnostics;

namespace MiaoNet.Server;

public sealed class TokenBucket
{
    private readonly int interval;
    private readonly int maxStored;

    private long stored;
    private long last;

    public TokenBucket(int interval, int maxStored)
    {
        this.interval = interval;
        this.maxStored = maxStored;
    }

    public bool TryConsume()
    {
        long now = GetTimestampMs();
        long elapsed = now - last;
        last = now;

        stored = Math.Min(maxStored, stored + elapsed);

        if (stored >= interval)
        {
            stored -= interval;
            return true;
        }

        return false;
    }

    private static long GetTimestampMs()
        => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
}