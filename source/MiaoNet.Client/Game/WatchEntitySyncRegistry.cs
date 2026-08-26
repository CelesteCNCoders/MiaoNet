using MiaoNet.Shared;
using System.Diagnostics;

namespace Celeste.Mod.MiaoNet;

[Flags]
internal enum WatchEntityApplyResult
{
    None = 0,
    SceneChanged = 1 << 0,
    RequiresRoomReload = 1 << 1,
}

internal readonly record struct WatchEntityApplySummary(
    WatchEntityApplyResult Result,
    IReadOnlyCollection<WatchEntityKind> RoomReloadRequestedKinds
);

internal interface IWatchEntityAdapter
{
    WatchEntityKind Kind { get; }

    IEnumerable<WatchEntityState> CaptureStates(Level level);

    WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    );

    void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }
}

internal static class WatchEntitySyncRegistry
{
    private static readonly SortedDictionary<WatchEntityKind, IWatchEntityAdapter> adapters = new();
    private static IWatchEntityAdapter[] orderedAdapters = [];
    private static readonly Dictionary<WatchEntityKey, WatchEntityState> emptyAdapterStates = [];
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
        orderedAdapters = adapters.Values.ToArray();
    }

    public static void Unregister(IWatchEntityAdapter adapter)
    {
        if (adapters.TryGetValue(adapter.Kind, out IWatchEntityAdapter? registered)
            && ReferenceEquals(adapter, registered))
        {
            adapters.Remove(adapter.Kind);
            orderedAdapters = adapters.Values.ToArray();
        }
    }

    public static WatchEntityStateTable.Capture CaptureStates(
        Level level,
        WatchRoomEntityIndex roomEntityIndex,
        WatchEntityStateTable stateTable,
        out HashSet<WatchEntityKind> unavailableKinds,
        bool resetCurrent = false,
        bool forceCurrent = false,
        WatchEntityCaptureCursor? captureCursor = null,
        long captureBudgetTicks = long.MaxValue
    )
    {
        using IDisposable captureScope = roomEntityIndex.BeginCapture(level);
        WatchEntityStateTable.Capture capture = stateTable.BeginCapture(resetCurrent);
        unavailableKinds = new();
        if (forceCurrent)
            forceCurrentCaptureDepth++;
        try
        {
            bool captureBudgeted = !resetCurrent && !forceCurrent && captureCursor is not null;
            int adapterCount = orderedAdapters.Length;
            int startIndex = captureBudgeted ? captureCursor!.GetStartIndex(adapterCount) : 0;
            int processedCount = 0;
            long captureStartedAt = Stopwatch.GetTimestamp();
            while (processedCount < adapterCount)
            {
                int adapterIndex = (startIndex + processedCount) % adapterCount;
                IWatchEntityAdapter adapter = orderedAdapters[adapterIndex];
                Dictionary<WatchEntityKey, WatchEntityState>? adapterStates = null;
                try
                {
                    foreach (WatchEntityState state in adapter.CaptureStates(level))
                    {
                        if (state.Key.Kind != adapter.Kind)
                        {
                            throw new InvalidOperationException(
                                $"Watch entity adapter {adapter.Kind} produced an invalid key."
                            );
                        }
                        adapterStates ??= new();
                        if (!adapterStates.TryAdd(state.Key, state))
                        {
                            adapterStates[state.Key] = state;
                            Logger.Warn(
                                LT.MiaoNetWatch,
                                $"Collapsed a duplicate transient watch entity key: {state.Key.Kind} " +
                                $"#{state.Key.EntityID}:{state.Key.SubID}."
                            );
                        }
                    }

                    capture.UpdateKind(adapter.Kind, adapterStates ?? emptyAdapterStates);
                }
                catch (Exception exception)
                {
                    unavailableKinds.Add(adapter.Kind);
                    Logger.Error(
                        LT.MiaoNetWatch,
                        $"Quarantined an invalid local watch update for {adapter.Kind}; " +
                        "the watch session will continue with its last known state when available."
                    );
                    Logger.LogDetailed(exception, LT.MiaoNetWatch);
                }

                processedCount++;
                if (captureBudgeted
                    && processedCount < adapterCount
                    && Stopwatch.GetTimestamp() - captureStartedAt >= captureBudgetTicks)
                    break;
            }

            if (captureBudgeted)
                captureCursor!.Advance(processedCount, adapterCount);
            else
                captureCursor?.Reset();
            return capture;
        }
        finally
        {
            if (forceCurrent)
                forceCurrentCaptureDepth--;
        }
    }

    public static WatchEntityApplySummary ApplyStates(
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
            HashSet<WatchEntityKind> roomReloadRequestedKinds = new();
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
                    WatchEntityApplyResult adapterResult = adapter.ApplyStates(
                        level,
                        statesByKind.GetValueOrDefault(adapter.Kind) ?? [],
                        isCompleteState
                    );
                    result |= adapterResult;
                    if (adapterResult.HasFlag(WatchEntityApplyResult.RequiresRoomReload))
                        roomReloadRequestedKinds.Add(adapter.Kind);
                }
                catch (Exception exception)
                {
                    roomReloadRequestedKinds.Add(adapter.Kind);
                    Logger.Error(
                        LT.MiaoNetWatch,
                        $"Failed to apply remote watch state for {adapter.Kind}; ignored this adapter update."
                    );
                    Logger.LogDetailed(exception, LT.MiaoNetWatch);
                }
            }
            return new(
                result,
                roomReloadRequestedKinds.Order().ToArray()
            );
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
