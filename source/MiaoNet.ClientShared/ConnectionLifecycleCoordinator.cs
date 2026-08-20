namespace MiaoNet.ClientShared;

internal enum ConnectionLifecycleState
{
    Idle,
    Connecting,
    Connected,
}

/// <summary>
/// Owns the generation and state transitions of a single logical connection slot.
/// Callers must still attach resources to a generation and guard callbacks with
/// <see cref="IsCurrent"/>.
/// </summary>
internal sealed class ConnectionLifecycleCoordinator
{
    private readonly object sync = new();
    private long nextGeneration;
    private long activeGeneration;
    private ConnectionLifecycleState state;

    internal ConnectionLifecycleState State
    {
        get
        {
            lock (sync)
                return state;
        }
    }

    internal long Begin()
    {
        lock (sync)
        {
            if (state is not ConnectionLifecycleState.Idle)
                throw new InvalidOperationException("A connection operation is already active.");

            activeGeneration = checked(++nextGeneration);
            state = ConnectionLifecycleState.Connecting;
            return activeGeneration;
        }
    }

    internal bool IsCurrent(long generation)
    {
        lock (sync)
            return state is not ConnectionLifecycleState.Idle && activeGeneration == generation;
    }

    internal bool TryMarkConnected(long generation)
    {
        lock (sync)
        {
            if (state is not ConnectionLifecycleState.Connecting || activeGeneration != generation)
                return false;

            state = ConnectionLifecycleState.Connected;
            return true;
        }
    }

    internal bool TryEnd(long generation)
    {
        lock (sync)
        {
            if (state is ConnectionLifecycleState.Idle || activeGeneration != generation)
                return false;

            activeGeneration = 0;
            state = ConnectionLifecycleState.Idle;
            return true;
        }
    }
}
