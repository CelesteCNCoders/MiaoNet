namespace MiaoNet.Server;

internal enum WatchResyncStartResult
{
    Started,
    Pending,
    Exhausted,
}

internal readonly record struct WatchResyncAttempt(
    int TargetID,
    int Generation,
    int Number
);

internal sealed class WatchResyncCoordinator
{
    private sealed class Operation
    {
        public int Attempts;
        public int Generation;
        public bool InFlight;
        public bool RetryScheduled;
    }

    private readonly int maxAttempts;
    private readonly Dictionary<int, Operation> operations = new();
    private int nextGeneration;

    public WatchResyncCoordinator(int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        this.maxAttempts = maxAttempts;
    }

    public WatchResyncStartResult TryStart(int targetID, out WatchResyncAttempt attempt)
    {
        Operation operation = GetOrCreate(targetID);
        if (operation.InFlight || operation.RetryScheduled)
        {
            attempt = default;
            return WatchResyncStartResult.Pending;
        }

        return Start(targetID, operation, out attempt);
    }

    public WatchResyncStartResult TryStartScheduled(
        int targetID,
        out WatchResyncAttempt attempt
    )
    {
        if (!operations.TryGetValue(targetID, out Operation? operation)
            || !operation.RetryScheduled)
        {
            attempt = default;
            return WatchResyncStartResult.Pending;
        }

        operation.RetryScheduled = false;
        return Start(targetID, operation, out attempt);
    }

    public bool TryFinishAttempt(int targetID, int generation)
    {
        if (!operations.TryGetValue(targetID, out Operation? operation)
            || !operation.InFlight
            || operation.Generation != generation)
            return false;

        operation.InFlight = false;
        return true;
    }

    public bool TryScheduleRetry(int targetID)
    {
        if (!operations.TryGetValue(targetID, out Operation? operation)
            || operation.InFlight
            || operation.RetryScheduled)
            return false;

        operation.RetryScheduled = true;
        return true;
    }

    public void Complete(int targetID)
        => operations.Remove(targetID);

    public bool HasOperation(int targetID)
        => operations.ContainsKey(targetID);

    private Operation GetOrCreate(int targetID)
    {
        if (!operations.TryGetValue(targetID, out Operation? operation))
        {
            operation = new();
            operations.Add(targetID, operation);
        }
        return operation;
    }

    private WatchResyncStartResult Start(
        int targetID,
        Operation operation,
        out WatchResyncAttempt attempt
    )
    {
        if (operation.Attempts >= maxAttempts)
        {
            attempt = default;
            return WatchResyncStartResult.Exhausted;
        }

        operation.Attempts++;
        operation.Generation = ++nextGeneration;
        operation.InFlight = true;
        attempt = new(targetID, operation.Generation, operation.Attempts);
        return WatchResyncStartResult.Started;
    }
}
