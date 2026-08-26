using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

[Flags]
internal enum WatchEntityApplyResult
{
    None = 0,
    SceneChanged = 1 << 0,
    RequiresRoomReload = 1 << 1,
}

internal interface IWatchEntityAdapter
{
    WatchEntityKind Kind { get; }

    IEnumerable<WatchEntityState> CaptureStates(Level level);

    WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    );

    void ApplyEvent(Level level, WatchEntityEvent entityEvent);
}

internal static class WatchEntitySyncRegistry
{
    private static readonly Dictionary<WatchEntityKind, IWatchEntityAdapter> adapters = new();
    private static int remoteApplyDepth;
    private static int forceCurrentCaptureDepth;
    private static int lifecycleResetApplyDepth;

    public static event Action<Level, WatchEntityEvent>? EventProduced;

    public static bool IsApplyingRemoteState => remoteApplyDepth > 0;

    public static bool IsCapturingCurrentState => forceCurrentCaptureDepth > 0;

    public static bool IsApplyingLifecycleReset => lifecycleResetApplyDepth > 0;

    public static void Register(IWatchEntityAdapter adapter)
    {
        if (adapter.Kind == WatchEntityKind.None || !adapters.TryAdd(adapter.Kind, adapter))
            throw new InvalidOperationException($"Invalid or duplicate watch entity adapter: {adapter.Kind}.");
    }

    public static void Unregister(IWatchEntityAdapter adapter)
    {
        if (adapters.TryGetValue(adapter.Kind, out IWatchEntityAdapter? registered)
            && ReferenceEquals(adapter, registered))
            adapters.Remove(adapter.Kind);
    }

    public static Dictionary<WatchEntityKey, WatchEntityState> CaptureStates(
        Level level,
        bool forceCurrent = false
    )
    {
        if (forceCurrent)
            forceCurrentCaptureDepth++;
        try
        {
            Dictionary<WatchEntityKey, WatchEntityState> states = new();
            foreach (IWatchEntityAdapter adapter in adapters.Values.OrderBy(adapter => adapter.Kind))
            {
                foreach (WatchEntityState state in adapter.CaptureStates(level))
                {
                    if (state.Key.Kind != adapter.Kind)
                    {
                        throw new InvalidOperationException(
                            $"Watch entity adapter {adapter.Kind} produced an invalid key."
                        );
                    }

                    if (!states.TryAdd(state.Key, state))
                    {
                        states[state.Key] = state;
                        Logger.Warn(
                            LT.MiaoNetWatch,
                            $"Collapsed a duplicate transient watch entity key: {state.Key.Kind} " +
                            $"#{state.Key.EntityID}:{state.Key.SubID}."
                        );
                    }
                }
            }
            return states;
        }
        finally
        {
            if (forceCurrent)
                forceCurrentCaptureDepth--;
        }
    }

    public static WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState,
        bool isLifecycleReset = false
    )
    {
        remoteApplyDepth++;
        if (isLifecycleReset)
            lifecycleResetApplyDepth++;
        IDisposable? audioSuppression = MiaoNetModule.Settings.PlayerAudioSyncMode.HasReceive
            ? null
            : WatchSceneAudioSuppression.Begin();
        try
        {
            WatchEntityApplyResult result = WatchEntityApplyResult.None;
            Dictionary<WatchEntityKind, List<WatchEntityState>> statesByKind = new();
            foreach (WatchEntityState state in states)
            {
                if (!statesByKind.TryGetValue(state.Key.Kind, out List<WatchEntityState>? kindStates))
                {
                    kindStates = new();
                    statesByKind.Add(state.Key.Kind, kindStates);
                }
                kindStates.Add(state);
            }
            IEnumerable<IWatchEntityAdapter> targetAdapters = isCompleteState
                ? adapters.Values
                : statesByKind.Keys.Select(kind => adapters.GetValueOrDefault(kind))
                    .OfType<IWatchEntityAdapter>();

            foreach (IWatchEntityAdapter adapter in targetAdapters)
            {
                try
                {
                    result |= adapter.ApplyStates(
                        level,
                        statesByKind.GetValueOrDefault(adapter.Kind) ?? [],
                        isCompleteState
                    );
                }
                catch (Exception exception)
                {
                    Logger.Error(
                        LT.MiaoNetWatch,
                        $"Failed to apply remote watch state for {adapter.Kind}; ignored this adapter update."
                    );
                    Logger.LogDetailed(exception, LT.MiaoNetWatch);
                }
            }
            return result;
        }
        finally
        {
            audioSuppression?.Dispose();
            if (isLifecycleReset)
                lifecycleResetApplyDepth--;
            remoteApplyDepth--;
        }
    }

    public static bool ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (!adapters.TryGetValue(entityEvent.Key.Kind, out IWatchEntityAdapter? adapter))
            return false;

        remoteApplyDepth++;
        IDisposable? audioSuppression = MiaoNetModule.Settings.PlayerAudioSyncMode.HasReceive
            ? null
            : WatchSceneAudioSuppression.Begin();
        try
        {
            try
            {
                adapter.ApplyEvent(level, entityEvent);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(
                    LT.MiaoNetWatch,
                    $"Failed to apply remote watch event for {entityEvent.Key.Kind} " +
                    $"#{entityEvent.Key.EntityID}:{entityEvent.Key.SubID}; ignored this event."
                );
                Logger.LogDetailed(exception, LT.MiaoNetWatch);
                return false;
            }
        }
        finally
        {
            audioSuppression?.Dispose();
            remoteApplyDepth--;
        }
    }

    public static void PublishEvent(Level level, WatchEntityEvent entityEvent)
        => EventProduced?.Invoke(level, entityEvent);
}
