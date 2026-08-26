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

        foreach (EntityData data in GetWingedStrawberryData(level))
        {
            WatchWingedStrawberryState state = WatchWingedStrawberryState.Absent;
            if (strawberriesByID.TryGetValue(data.ID, out Strawberry? strawberry)
                && strawberry.Follower.Leader is null)
            {
                state = strawberry.flyingAway
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
        Dictionary<int, Strawberry> strawberriesByID = WatchRoomEntityIndex.Enumerate<Strawberry>(level)
            .Where(strawberry => strawberry.ID.Level == room)
            .GroupBy(strawberry => strawberry.ID.ID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach ((int id, WatchWingedStrawberryState state) in stateByID)
        {
            strawberriesByID.TryGetValue(id, out Strawberry? strawberry);
            switch (state)
            {
                case WatchWingedStrawberryState.Present:
                    if (strawberry is null || strawberry.Follower.Leader is not null || strawberry.flyingAway)
                        requiresReload = true;
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

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.SceneChanged | WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }


    private static IEnumerable<EntityData> GetWingedStrawberryData(Level level)
        => level.Session.LevelData.Entities.Where(data =>
            data.Name == "strawberry" && data.Bool("winged")
        );
}
