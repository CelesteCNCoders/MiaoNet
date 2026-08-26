using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchDashBlockAdapter : IWatchEntityAdapter
{
    private const byte Present = 1;
    private const byte BreakEvent = 1;
    private const int BreakPayloadSize = 18;

    private static readonly WatchDashBlockAdapter instance = new();
    private static readonly Dictionary<string, HashSet<int>> brokenByRoom = new(StringComparer.Ordinal);

    public WatchEntityKind Kind => WatchEntityKind.DashBlock;

    public static void Load()
    {
        On.Celeste.DashBlock.ctor_EntityData_Vector2_EntityID += DashBlock_ctor;
        On.Celeste.DashBlock.Break_Vector2_Vector2_bool_bool += DashBlock_Break;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.DashBlock.Break_Vector2_Vector2_bool_bool -= DashBlock_Break;
        On.Celeste.DashBlock.ctor_EntityData_Vector2_EntityID -= DashBlock_ctor;
        brokenByRoom.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> liveIDs = new();
        foreach (DashBlock block in level.Entities.OfType<DashBlock>())
        {
            if (block.id.Level != room)
                continue;

            liveIDs.Add(block.id.ID);
            yield return Encode(block.id.ID, present: true);
        }

        if (!brokenByRoom.TryGetValue(room, out HashSet<int>? brokenIDs))
            yield break;

        foreach (int id in brokenIDs.Where(id => !liveIDs.Contains(id)).Order())
            yield return Encode(id, present: false);
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, bool> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] > Present
                || !desiredByID.TryAdd(state.Key.EntityID, state.Payload.Span[0] == Present))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        bool requiresReload = false;
        string room = level.Session.Level;
        if (isCompleteState)
            changed |= RestoreMissingBlocks(level, desiredByID);
        foreach (DashBlock block in level.Entities.OfType<DashBlock>().ToArray())
        {
            if (block.id.Level != room)
                continue;

            if (!desiredByID.Remove(block.id.ID, out bool present))
            {
                requiresReload |= isCompleteState;
                continue;
            }

            if (present)
            {
                if (!block.Visible || !block.Collidable)
                {
                    block.Visible = true;
                    block.Collidable = true;
                    block.EnableStaticMovers();
                    changed = true;
                }
            }
            else if (block.Visible || block.Collidable)
            {
                block.Visible = false;
                block.Collidable = false;
                block.DisableStaticMovers();
                changed = true;
            }
        }

        if (desiredByID.Values.Any(present => present))
            requiresReload = true;

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    private static bool RestoreMissingBlocks(
        Level level,
        IReadOnlyDictionary<int, bool> desiredByID
    )
    {
        string room = level.Session.Level;
        HashSet<int> existing = level.Entities.OfType<DashBlock>()
            .Where(block => block.id.Level == room)
            .Select(block => block.id.ID)
            .ToHashSet();
        int[] missing = desiredByID
            .Where(pair => pair.Value && !existing.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToArray();
        if (missing.Length == 0)
            return false;

        LevelData levelData = level.Session.MapData.Get(room);
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        HashSet<int> missingSet = missing.ToHashSet();
        int restored = 0;
        foreach (EntityData data in levelData.Entities)
        {
            if (!missingSet.Remove(data.ID))
                continue;

            EntityID id = new(room, data.ID);
            DashBlock block = new(data, offset, id)
            {
                SourceData = data,
                SourceId = id,
            };
            level.Add(block);
            restored++;
        }

        if (restored > 0)
        {
            level.Entities.UpdateLists();
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Restored {restored} DashBlock instance(s) in-place for room {room}."
            );
        }
        return restored > 0;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.Key.Kind != Kind
            || entityEvent.Key.SubID != 0
            || entityEvent.EventID != BreakEvent
            || entityEvent.Payload.Length != BreakPayloadSize)
            return;

        ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
        Vector2 from = WatchEntityPayloadCodec.ReadVector2(payload, 0);
        Vector2 direction = WatchEntityPayloadCodec.ReadVector2(payload, 8);
        bool playSound = payload[16] != 0;
        bool playDebrisSound = payload[17] != 0;
        DashBlock? block = level.Entities.OfType<DashBlock>().FirstOrDefault(candidate =>
            candidate.id.Level == level.Session.Level
            && candidate.id.ID == entityEvent.Key.EntityID
        );
        if (block is null)
            return;

        block.Visible = true;
        block.Collidable = true;
        block.Break(from, direction, playSound, playDebrisSound);
    }

    private static WatchEntityState Encode(int id, bool present)
        => new(new WatchEntityKey(WatchEntityKind.DashBlock, id), [present ? Present : (byte)0]);

    private static void DashBlock_ctor(
        On.Celeste.DashBlock.orig_ctor_EntityData_Vector2_EntityID orig,
        DashBlock self,
        EntityData data,
        Vector2 offset,
        EntityID id
    )
    {
        orig(self, data, offset, id);
        if (brokenByRoom.TryGetValue(id.Level, out HashSet<int>? brokenIDs))
            brokenIDs.Remove(id.ID);
    }

    private static void DashBlock_Break(
        On.Celeste.DashBlock.orig_Break_Vector2_Vector2_bool_bool orig,
        DashBlock self,
        Vector2 from,
        Vector2 direction,
        bool playSound,
        bool playDebrisSound
    )
    {
        Level? level = self.Scene as Level;
        EntityID id = self.id;
        bool publish = level is not null
            && !WatchEntitySyncRegistry.IsApplyingRemoteState;

        orig(self, from, direction, playSound, playDebrisSound);
        if (!publish)
            return;

        if (!brokenByRoom.TryGetValue(id.Level, out HashSet<int>? brokenIDs))
            brokenByRoom[id.Level] = brokenIDs = new();
        brokenIDs.Add(id.ID);

        byte[] payload = new byte[BreakPayloadSize];
        WatchEntityPayloadCodec.WriteVector2(payload, 0, from);
        WatchEntityPayloadCodec.WriteVector2(payload, 8, direction);
        payload[16] = playSound ? (byte)1 : (byte)0;
        payload[17] = playDebrisSound ? (byte)1 : (byte)0;
        WatchEntitySyncRegistry.PublishEvent(
            level!,
            new WatchEntityEvent(
                new WatchEntityKey(WatchEntityKind.DashBlock, id.ID),
                BreakEvent,
                payload
            )
        );
    }
}
