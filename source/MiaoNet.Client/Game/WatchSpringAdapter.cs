using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchSpringAdapter : IWatchEntityAdapter
{
    private const byte BounceEvent = 1;
    private static readonly WatchSpringAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.Spring;

    private WatchSpringAdapter()
    {
    }

    public static void Load()
    {
        On.Celeste.Spring.ctor_EntityData_Vector2_Orientations += Spring_ctor;
        On.Celeste.Spring.BounceAnimate += Spring_BounceAnimate;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Spring.BounceAnimate -= Spring_BounceAnimate;
        On.Celeste.Spring.ctor_EntityData_Vector2_Orientations -= Spring_ctor;
        WatchEntityIDTable<Spring>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Spring spring in WatchRoomEntityIndex.Enumerate<Spring>(level))
        {
            if (WatchEntityIDTable<Spring>.TryGet(spring, room, out int id))
            {
                yield return WatchEntityState.FromTyped(
                    new(Kind, id),
                    spring.playerCanUse,
                    static value => [value ? (byte)1 : (byte)0]
                );
            }
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, bool> enabledByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] > 1
                || !enabledByID.TryAdd(state.Key.EntityID, state.Payload.Span[0] != 0))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (Spring spring in WatchRoomEntityIndex.Enumerate<Spring>(level))
        {
            if (!WatchEntityIDTable<Spring>.TryGet(spring, room, out int id)
                || !enabledByID.TryGetValue(id, out bool enabled)
                || spring.playerCanUse == enabled)
                continue;

            if (enabled)
                spring.OnEnable();
            else
                spring.OnDisable();
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != BounceEvent || entityEvent.Payload.Length != 0)
            return;

        Spring? spring = WatchEntityIDTable<Spring>.Find(level, entityEvent.Key.EntityID);
        spring?.BounceAnimate();
    }

    private static void Spring_ctor(
        On.Celeste.Spring.orig_ctor_EntityData_Vector2_Orientations orig,
        Spring self,
        EntityData data,
        Vector2 offset,
        Spring.Orientations orientation
    )
    {
        orig(self, data, offset, orientation);
        WatchEntityIDTable<Spring>.Set(self, data.Level.Name, data.ID);
    }

    private static void Spring_BounceAnimate(
        On.Celeste.Spring.orig_BounceAnimate orig,
        Spring self
    )
    {
        orig(self);
        if (WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level
            || !WatchEntityIDTable<Spring>.TryGet(self, level.Session.Level, out int id))
            return;

        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Spring, id), BounceEvent, [])
        );
    }
}
