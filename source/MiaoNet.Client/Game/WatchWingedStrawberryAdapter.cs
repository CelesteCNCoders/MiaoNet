using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchWingedStrawberryAdapter : IWatchEntityAdapter
{
    private static readonly WatchWingedStrawberryAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.WingedStrawberry;

    private WatchWingedStrawberryAdapter()
    {
    }

    public static void Load()
        => WatchEntitySyncRegistry.Register(instance);

    public static void Unload()
        => WatchEntitySyncRegistry.Unregister(instance);

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        Dictionary<int, Strawberry> strawberriesByID = WatchRoomEntityIndex.Enumerate<Strawberry>(level)
            .Where(strawberry => strawberry.ID.Level == room)
            .GroupBy(strawberry => strawberry.ID.ID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (EntityData data in GetLifecycleStrawberryData(level))
        {
            WatchWingedStrawberryState state = WatchWingedStrawberryState.Absent;
            if (strawberriesByID.TryGetValue(data.ID, out Strawberry? strawberry)
                && strawberry.Follower.Leader is null)
            {
                state = WatchStrawberryLifecycle.SupportsFlyingAway(
                    data.Name,
                    data.Bool("winged")
                ) && strawberry.flyingAway
                    ? WatchWingedStrawberryState.FlyingAway
                    : WatchWingedStrawberryState.Present;
            }

            yield return WatchEntityState.FromTyped(
                new(Kind, data.ID),
                (byte)state,
                static value => [value]
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchWingedStrawberryState> stateByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] > (byte)WatchWingedStrawberryState.Absent
                || !stateByID.TryAdd(
                    state.Key.EntityID,
                    (WatchWingedStrawberryState)state.Payload.Span[0]
                ))
            {
                Logger.Warn(LT.MiaoNetWatch, "Ignored invalid WingedStrawberry watch state.");
                return WatchEntityApplyResult.None;
            }
        }

        bool changed = false;
        bool requiresReload = false;
        string room = level.Session.Level;
        Dictionary<int, EntityData> dataByID = GetLifecycleStrawberryData(level)
            .GroupBy(data => data.ID)
            .ToDictionary(group => group.Key, group => group.First());
        foreach ((int id, WatchWingedStrawberryState state) in stateByID)
        {
            if (!dataByID.TryGetValue(id, out EntityData? data)
                || !WatchStrawberryLifecycle.IsValidState(
                    data.Name,
                    data.Bool("winged"),
                    state
                ))
            {
                Logger.Warn(
                    LT.MiaoNetWatch,
                    $"Ignored WingedStrawberry watch state for an incompatible room entity: {id}."
                );
                return WatchEntityApplyResult.None;
            }
        }

        Dictionary<int, Strawberry> strawberriesByID = WatchRoomEntityIndex.Enumerate<Strawberry>(level)
            .Where(strawberry => strawberry.ID.Level == room)
            .GroupBy(strawberry => strawberry.ID.ID)
            .ToDictionary(group => group.Key, group => group.First());
        List<Strawberry> restoredGoldenBerries = [];

        foreach ((int id, WatchWingedStrawberryState state) in stateByID)
        {
            strawberriesByID.TryGetValue(id, out Strawberry? strawberry);
            switch (state)
            {
                case WatchWingedStrawberryState.Present:
                    if (strawberry is null
                        && WatchStrawberryLifecycle.IsGoldenBerry(dataByID[id].Name))
                    {
                        strawberry = RestoreGoldenBerry(level, dataByID[id]);
                        strawberriesByID.Add(id, strawberry);
                        restoredGoldenBerries.Add(strawberry);
                        changed = true;
                    }
                    else if (strawberry is null
                        || strawberry.Follower.Leader is not null
                        || strawberry.flyingAway)
                    {
                        requiresReload = true;
                    }
                    break;

                case WatchWingedStrawberryState.FlyingAway:
                    if (strawberry is not null
                        && strawberry.Follower.Leader is null
                        && !strawberry.flyingAway)
                    {
                        strawberry.OnDash(Vector2.Zero);
                        changed = true;
                    }
                    break;

                case WatchWingedStrawberryState.Absent:
                    if (strawberry is not null)
                    {
                        strawberry.RemoveSelf();
                        changed = true;
                    }
                    break;
            }
        }

        if (restoredGoldenBerries.Count > 0)
        {
            level.Entities.UpdateLists();
            foreach (Strawberry strawberry in restoredGoldenBerries)
                WatchPersistentSessionAdapter.ApplyRemoteStrawberryAppearance(level, strawberry);
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Restored {restoredGoldenBerries.Count} Golden Berry instance(s) in room {room}."
            );
        }

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.SceneChanged | WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }


    private static Strawberry RestoreGoldenBerry(Level level, EntityData data)
    {
        string room = level.Session.Level;
        EntityID id = new(room, data.ID);
        LevelData levelData = level.Session.LevelData;
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        Strawberry strawberry = new(data, offset, id)
        {
            SourceData = data,
            SourceId = id,
        };
        level.Add(strawberry);
        return strawberry;
    }

    private static IEnumerable<EntityData> GetLifecycleStrawberryData(Level level)
        => level.Session.LevelData.Entities.Where(data =>
            WatchStrawberryLifecycle.IsTrackedMapEntity(data.Name, data.Bool("winged"))
        );
}
