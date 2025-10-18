using System.Collections.Concurrent;

namespace Celeste.Mod.MiaoNet;

public sealed class SingleThreadedSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback, object?)> callbacks;

    public SingleThreadedSynchronizationContext()
    {
        callbacks = new();
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        callbacks.Add((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        d(state);
    }

    public void ProcessLoop()
    {
        SetSynchronizationContext(this);
        foreach (var item in callbacks.GetConsumingEnumerable())
        {
            item.Item1(item.Item2);
        }
    }
}
