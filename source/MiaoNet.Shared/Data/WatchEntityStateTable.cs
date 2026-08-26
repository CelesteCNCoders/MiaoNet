namespace MiaoNet.Shared;

internal sealed class WatchEntityStateTable
{
    private static readonly WatchEntityKind[] orderedKinds = Enum.GetValues<WatchEntityKind>()
        .Where(kind => kind != WatchEntityKind.None)
        .Order()
        .ToArray();

    private readonly Dictionary<WatchEntityKind, Dictionary<WatchEntityKey, WatchEntityState>>
        statesByKind = new();
    private int version;

    internal int Count { get; private set; }

    internal Capture BeginCapture(bool resetCurrent = false)
        => new(this, resetCurrent, version);

    internal void Clear()
    {
        statesByKind.Clear();
        Count = 0;
        version++;
    }

    private IReadOnlyDictionary<WatchEntityKey, WatchEntityState> GetStates(
        WatchEntityKind kind
    ) => statesByKind.GetValueOrDefault(kind)
        ?? EmptyReadOnlyDictionary<WatchEntityKey, WatchEntityState>.Instance;

    private sealed class EmptyReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        internal static readonly IReadOnlyDictionary<TKey, TValue> Instance =
            new Dictionary<TKey, TValue>();
    }

    internal sealed class Capture
    {
        private readonly WatchEntityStateTable owner;
        private readonly bool resetCurrent;
        private readonly int ownerVersion;
        private readonly Dictionary<WatchEntityKind, Dictionary<WatchEntityKey, WatchEntityState>>
            replacements = new();
        private readonly HashSet<WatchEntityKind> capturedKinds = new();
        private readonly List<WatchEntityState> changedStates = new();
        private bool committed;

        internal int CurrentCount { get; private set; }

        internal bool HasRemovals { get; private set; }

        internal bool HasChanges => HasRemovals || changedStates.Count > 0;

        internal Capture(WatchEntityStateTable owner, bool resetCurrent, int ownerVersion)
        {
            this.owner = owner;
            this.resetCurrent = resetCurrent;
            this.ownerVersion = ownerVersion;
            CurrentCount = resetCurrent ? 0 : owner.Count;
        }

        internal void UpdateKind(
            WatchEntityKind kind,
            Dictionary<WatchEntityKey, WatchEntityState> currentStates
        )
        {
            if (kind == WatchEntityKind.None || !capturedKinds.Add(kind))
                throw new InvalidOperationException($"Invalid or duplicate captured kind: {kind}.");

            IReadOnlyDictionary<WatchEntityKey, WatchEntityState> previousStates = resetCurrent
                ? EmptyReadOnlyDictionary<WatchEntityKey, WatchEntityState>.Instance
                : owner.GetStates(kind);
            bool changed = currentStates.Count != previousStates.Count;
            bool hasRemovals = previousStates.Keys.Any(key => !currentStates.ContainsKey(key));
            List<WatchEntityState>? currentChangedStates = null;

            foreach ((WatchEntityKey key, WatchEntityState state) in currentStates)
            {
                if (key.Kind != kind)
                    throw new InvalidOperationException($"Captured {kind} state used a {key.Kind} key.");

                bool hasPrevious = previousStates.TryGetValue(key, out WatchEntityState previous);
                if (hasPrevious
                    && state.TryTypedStateEquals(previous, out bool typedEquals)
                    && typedEquals)
                    continue;

                if (!WatchPacketValidator.IsValid(state))
                {
                    throw new InvalidOperationException(
                        $"Captured {kind} an invalid state for " +
                        $"#{key.EntityID}:{key.SubID} ({state.Payload.Length} bytes)."
                    );
                }
                if (hasPrevious && state.Payload.Span.SequenceEqual(previous.Payload.Span))
                    continue;

                changed = true;
                (currentChangedStates ??= []).Add(state);
            }

            if (!changed)
                return;

            replacements.Add(kind, currentStates);
            HasRemovals |= hasRemovals;
            if (currentChangedStates is not null)
                changedStates.AddRange(currentChangedStates);
            CurrentCount += currentStates.Count - previousStates.Count;
        }

        internal WatchEntityStateMode GetStateMode(bool forceCompleteState)
        {
            if (forceCompleteState || HasRemovals)
                return WatchEntityStateMode.Replace;
            return changedStates.Count == 0
                ? WatchEntityStateMode.None
                : WatchEntityStateMode.Patch;
        }

        internal IEnumerable<WatchEntityState> GetStates(WatchEntityStateMode mode)
            => mode switch
            {
                WatchEntityStateMode.None => [],
                WatchEntityStateMode.Patch => changedStates,
                WatchEntityStateMode.Replace => EnumerateCurrentStates(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

        internal IEnumerable<WatchEntityState> EnumerateCurrentStates()
        {
            foreach (WatchEntityKind kind in orderedKinds)
            {
                IReadOnlyDictionary<WatchEntityKey, WatchEntityState> states;
                if (replacements.TryGetValue(kind, out Dictionary<WatchEntityKey, WatchEntityState>? replacement))
                    states = replacement;
                else if (resetCurrent)
                    continue;
                else
                    states = owner.GetStates(kind);

                foreach (WatchEntityState state in states.Values)
                    yield return state;
            }
        }

        internal void Commit()
        {
            if (committed)
                throw new InvalidOperationException("Watch entity capture was already committed.");
            if (owner.version != ownerVersion)
                throw new InvalidOperationException("Watch entity state table changed during capture.");

            if (resetCurrent)
                owner.statesByKind.Clear();
            foreach ((WatchEntityKind kind, Dictionary<WatchEntityKey, WatchEntityState> states) in replacements)
            {
                if (states.Count == 0)
                    owner.statesByKind.Remove(kind);
                else
                    owner.statesByKind[kind] = states;
            }

            owner.Count = CurrentCount;
            owner.version++;
            committed = true;
        }
    }
}
