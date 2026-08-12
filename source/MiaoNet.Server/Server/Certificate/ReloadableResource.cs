namespace MiaoNet.Server;

internal sealed class ReloadableResource<T> : IDisposable where T : class, IDisposable
{
    private readonly object gate = new();
    private Entry? current;
    private bool disposed;

    public ReloadableResource(T initialValue)
    {
        ArgumentNullException.ThrowIfNull(initialValue);
        current = new Entry(initialValue);
    }

    public ResourceLease<T> Acquire()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var entry = current!;
            entry.LeaseCount++;
            return new ResourceLease<T>(entry.Value, () => Release(entry));
        }
    }

    public void Replace(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Entry? entryToDispose;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var previous = current!;
            current = new Entry(value);
            entryToDispose = Retire(previous);
        }

        entryToDispose?.Value.Dispose();
    }

    public void Dispose()
    {
        Entry? entryToDispose;
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            entryToDispose = Retire(current!);
            current = null;
        }

        entryToDispose?.Value.Dispose();
    }

    private void Release(Entry entry)
    {
        bool shouldDispose;
        lock (gate)
        {
            entry.LeaseCount--;
            shouldDispose = entry.Retired && entry.LeaseCount == 0;
        }

        if (shouldDispose)
            entry.Value.Dispose();
    }

    private static Entry? Retire(Entry entry)
    {
        entry.Retired = true;
        return entry.LeaseCount == 0 ? entry : null;
    }

    private sealed class Entry(T value)
    {
        public T Value { get; } = value;
        public int LeaseCount { get; set; }
        public bool Retired { get; set; }
    }
}

internal sealed class ResourceLease<T>(T value, Action release) : IDisposable where T : class, IDisposable
{
    private Action? release = release;

    public T Value { get; } = value;

    public void Dispose()
        => Interlocked.Exchange(ref release, null)?.Invoke();
}
