namespace Celeste.Mod.MiaoNet;

internal enum WatchPlaybackEnqueueResult
{
    Success,
    CapacityExceeded,
    TimestampRegression,
}

internal readonly record struct WatchPlaybackEntry<T>(long ReceivedAt, T Value);

internal static class WatchPlaybackTiming
{
    internal const int DelayFrames = 15;
    internal const int NominalFramesPerSecond = 60;

    internal static long GetDelayTicks(long timestampFrequency)
    {
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        return checked(timestampFrequency * DelayFrames / NominalFramesPerSecond);
    }

    internal static float GetInterpolationAmount(long from, long to, long playbackTime)
    {
        if (to <= from)
            return 1f;
        return Math.Clamp((playbackTime - from) / (float)(to - from), 0f, 1f);
    }
}

internal sealed class WatchPlaybackQueue<T>
{
    private readonly Queue<WatchPlaybackEntry<T>> entries = new();
    private readonly int capacity;
    private long newestReceivedAt;

    internal int Count => entries.Count;

    internal WatchPlaybackQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    internal WatchPlaybackEnqueueResult Enqueue(long receivedAt, T value)
    {
        if (receivedAt < 0)
            throw new ArgumentOutOfRangeException(nameof(receivedAt));
        if (entries.Count >= capacity)
            return WatchPlaybackEnqueueResult.CapacityExceeded;
        if (entries.Count > 0 && receivedAt < newestReceivedAt)
            return WatchPlaybackEnqueueResult.TimestampRegression;

        entries.Enqueue(new(receivedAt, value));
        newestReceivedAt = receivedAt;
        return WatchPlaybackEnqueueResult.Success;
    }

    internal bool TryDequeueDue(long playbackTime, out WatchPlaybackEntry<T> entry)
    {
        if (entries.TryPeek(out WatchPlaybackEntry<T> next)
            && next.ReceivedAt <= playbackTime)
        {
            entry = entries.Dequeue();
            if (entries.Count == 0)
                newestReceivedAt = 0;
            return true;
        }

        entry = default;
        return false;
    }

    internal bool TryPeek(out WatchPlaybackEntry<T> entry)
        => entries.TryPeek(out entry);

    internal void Clear()
    {
        entries.Clear();
        newestReceivedAt = 0;
    }
}
