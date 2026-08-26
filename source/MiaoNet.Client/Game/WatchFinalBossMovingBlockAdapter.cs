using MiaoNet.Shared;
using System.Buffers.Binary;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchFinalBossMovingBlockAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 36;
    private const float AnchorInterval = 0.1f;
    private const float HardReanchorDistance = 96f;

    private const byte StartEvent = 1;
    private const byte BreakEvent = 2;

    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte HighlightedFlag = 1 << 2;
    private const byte MovingFlag = 1 << 3;

    private readonly record struct BlockState(
        byte Flags,
        int BossNodeIndex,
        int NodeIndex,
        Vector2 Position,
        Vector2 MovementCounter,
        float StartDelay,
        float HighlightAlpha
    );

    private readonly record struct SyncSignature(byte Flags, int NodeIndex);

    private sealed class SyncInfo
    {
        private bool hasState;
        private SyncSignature signature;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, BlockState current, bool force, float time)
        {
            SyncSignature currentSignature = new(current.Flags, current.NodeIndex);
            bool movingAnchor = (current.Flags & MovingFlag) != 0 && time >= nextAnchorTime;
            if (force || !hasState || signature != currentSignature || movingAnchor)
            {
                state = Encode(id, current);
                signature = currentSignature;
                hasState = true;
                nextAnchorTime = time + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public BlockState State { get; set; }
        public Vector2 Start { get; set; }
        public Vector2 Target { get; set; }
        public float Elapsed { get; set; }
        public float Duration { get; set; }
        public float StartHighlightAlpha { get; set; }
        public float TargetHighlightAlpha { get; set; }
        public bool Finished { get; set; }
    }

    private static readonly WatchFinalBossMovingBlockAdapter instance = new();
    private static readonly ConditionalWeakTable<FinalBossMovingBlock, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<FinalBossMovingBlock, RemoteInfo> remoteInfo = new();
    private static int remoteFinishDepth;

    public WatchEntityKind Kind => WatchEntityKind.FinalBossMovingBlock;

    public static void Load()
    {
        On.Celeste.FinalBossMovingBlock.ctor_EntityData_Vector2 += Block_ctor;
        On.Celeste.FinalBossMovingBlock.StartMoving += Block_StartMoving;
        On.Celeste.FinalBossMovingBlock.MoveSequence += Block_MoveSequence;
        On.Celeste.FinalBossMovingBlock.Destroy += Block_Destroy;
        On.Celeste.FinalBossMovingBlock.Finish += Block_Finish;
        On.Celeste.Level.Update += Level_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Level.Update -= Level_Update;
        On.Celeste.FinalBossMovingBlock.Finish -= Block_Finish;
        On.Celeste.FinalBossMovingBlock.Destroy -= Block_Destroy;
        On.Celeste.FinalBossMovingBlock.MoveSequence -= Block_MoveSequence;
        On.Celeste.FinalBossMovingBlock.StartMoving -= Block_StartMoving;
        On.Celeste.FinalBossMovingBlock.ctor_EntityData_Vector2 -= Block_ctor;
        WatchEntityIDTable<FinalBossMovingBlock>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (FinalBossMovingBlock block in level.Entities.OfType<FinalBossMovingBlock>())
        {
            if (!WatchEntityIDTable<FinalBossMovingBlock>.TryGet(block, room, out int id))
                continue;
            yield return syncInfo.GetValue(block, static _ => new()).Capture(
                id,
                Capture(block),
                WatchEntitySyncRegistry.IsCapturingCurrentState,
                level.TimeActive
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, BlockState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out BlockState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        Dictionary<int, FinalBossMovingBlock> existing = level.Entities
            .OfType<FinalBossMovingBlock>()
            .Select(block => (
                Block: block,
                HasID: WatchEntityIDTable<FinalBossMovingBlock>.TryGet(block, room, out int id),
                ID: id
            ))
            .Where(item => item.HasID)
            .GroupBy(item => item.ID)
            .ToDictionary(group => group.Key, group => group.First().Block);

        foreach ((int id, BlockState state) in desired)
        {
            if (!existing.Remove(id, out FinalBossMovingBlock? block))
            {
                block = Recreate(level, id);
                if (block is null)
                    continue;
                changed = true;
            }
            changed |= ApplyAnchor(block, state);
        }

        if (isCompleteState)
        {
            foreach (FinalBossMovingBlock block in existing.Values)
            {
                changed = true;
                block.DestroyStaticMovers();
                block.RemoveSelf();
                remoteInfo.GetValue(block, static _ => new()).HasState = false;
            }

            if (WatchEntitySyncRegistry.IsApplyingLifecycleReset && desired.Count > 0)
                changed |= RestoreMissingMapSpikes(level);
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        FinalBossMovingBlock? block = Find(level, entityEvent.Key.EntityID);
        if (block is null || entityEvent.Payload.Length != 0)
            return;
        if (entityEvent.EventID == StartEvent)
        {
            block.StartShaking(0.1f);
        }
        else if (entityEvent.EventID == BreakEvent)
        {
            RemoteInfo info = remoteInfo.GetValue(block, static _ => new());
            if (info.Finished)
                return;
            info.Finished = true;
            remoteFinishDepth++;
            try
            {
                block.Finish();
            }
            finally
            {
                remoteFinishDepth--;
            }
        }
    }

    private static BlockState Capture(FinalBossMovingBlock block)
    {
        byte flags = 0;
        if (block.Visible) flags |= VisibleFlag;
        if (block.Collidable) flags |= CollidableFlag;
        if (block.isHighlighted) flags |= HighlightedFlag;
        if (block.moveCoroutine?.Active == true) flags |= MovingFlag;
        return new(
            flags,
            Math.Max(0, block.BossNodeIndex),
            Math.Max(0, block.nodeIndex),
            block.Position,
            block.movementCounter,
            block.startDelay,
            Math.Clamp(block.highlight.Alpha, 0f, 1f)
        );
    }

    private static WatchEntityState Encode(int id, BlockState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), state.BossNodeIndex);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), state.NodeIndex);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.MovementCounter.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.MovementCounter.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.StartDelay);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.HighlightAlpha);
        return new(new WatchEntityKey(WatchEntityKind.FinalBossMovingBlock, id), payload);
    }

    private static bool TryDecode(WatchEntityState state, out BlockState value)
    {
        value = default;
        ReadOnlySpan<byte> p = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.FinalBossMovingBlock || state.Key.SubID != 0
            || p.Length != PayloadSize || (p[0] & ~0b0000_1111) != 0
            || p[1] != 0 || p[2] != 0 || p[3] != 0)
            return false;
        Vector2 position = new(
            WatchEntityPayloadCodec.ReadSingle(p, 12),
            WatchEntityPayloadCodec.ReadSingle(p, 16)
        );
        Vector2 movement = new(
            WatchEntityPayloadCodec.ReadSingle(p, 20),
            WatchEntityPayloadCodec.ReadSingle(p, 24)
        );
        float delay = WatchEntityPayloadCodec.ReadSingle(p, 28);
        float highlightAlpha = WatchEntityPayloadCodec.ReadSingle(p, 32);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(movement.X) || !float.IsFinite(movement.Y)
            || !float.IsFinite(delay) || highlightAlpha is < 0f or > 1f)
            return false;
        value = new(
            p[0],
            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(p[8..]),
            position,
            movement,
            delay,
            highlightAlpha
        );
        return true;
    }

    private static bool ApplyAnchor(FinalBossMovingBlock block, BlockState state)
    {
        RemoteInfo applied = remoteInfo.GetValue(block, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState || applied.State.NodeIndex != state.NodeIndex
            || Vector2.DistanceSquared(block.Position, state.Position)
                >= HardReanchorDistance * HardReanchorDistance;
        bool changed = !applied.HasState || applied.State != state;
        block.Visible = (state.Flags & VisibleFlag) != 0;
        block.Collidable = false;
        block.isHighlighted = (state.Flags & HighlightedFlag) != 0;
        block.BossNodeIndex = state.BossNodeIndex;
        block.nodeIndex = state.NodeIndex;
        block.startDelay = state.StartDelay;
        if (block.moveCoroutine is not null)
            block.moveCoroutine.Active = false;

        if (hard)
        {
            block.MoveTo(state.Position);
            block.ClearRemainder();
            applied.Start = applied.Target = state.Position;
            applied.Elapsed = applied.Duration = 0f;
            applied.StartHighlightAlpha = applied.TargetHighlightAlpha = state.HighlightAlpha;
            ApplyHighlight(block, state.HighlightAlpha);
        }
        else
        {
            applied.Start = block.Position;
            applied.Target = state.Position;
            applied.StartHighlightAlpha = block.highlight.Alpha;
            applied.TargetHighlightAlpha = state.HighlightAlpha;
            applied.Elapsed = 0f;
            applied.Duration = AnchorInterval;
        }
        applied.State = state;
        applied.HasState = true;
        applied.Finished = false;
        return changed;
    }

    private static void MaintainRemoteStates(Level level)
    {
        if (!MiaoNetModule.IsWatching || MiaoNetModule.IsWatchedPlayerPaused)
            return;
        foreach (FinalBossMovingBlock block in level.Entities.OfType<FinalBossMovingBlock>())
        {
            if (!remoteInfo.TryGetValue(block, out RemoteInfo? applied) || !applied.HasState
                || applied.Duration <= 0f)
                continue;
            applied.Elapsed = Math.Min(applied.Elapsed + Engine.DeltaTime, applied.Duration);
            Vector2 target = Vector2.Lerp(
                applied.Start,
                applied.Target,
                applied.Elapsed / applied.Duration
            );
            block.MoveTo(target);
            block.ClearRemainder();
            ApplyHighlight(block, MathHelper.Lerp(
                applied.StartHighlightAlpha,
                applied.TargetHighlightAlpha,
                applied.Elapsed / applied.Duration
            ));
            block.Collidable = false;
        }
    }

    private static void ApplyHighlight(FinalBossMovingBlock block, float highlightAlpha)
    {
        highlightAlpha = Math.Clamp(highlightAlpha, 0f, 1f);
        block.highlight.Alpha = highlightAlpha;
        block.sprite.Alpha = 1f - highlightAlpha;
    }

    private static FinalBossMovingBlock? Recreate(Level level, int id)
    {
        LevelData levelData = level.Session.LevelData;
        EntityData? data = levelData.Entities.FirstOrDefault(candidate =>
            candidate.ID == id && candidate.Name == "finalBossMovingBlock"
        );
        if (data is null)
            return null;
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        FinalBossMovingBlock block = new(data, offset);
        WatchEntityIDTable<FinalBossMovingBlock>.Set(block, level.Session.Level, id);
        level.Add(block);
        return block;
    }

    private static bool RestoreMissingMapSpikes(Level level)
    {
        LevelData levelData = level.Session.LevelData;
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        List<Spikes> existing = level.Entities.OfType<Spikes>().ToList();
        existing.AddRange(level.Entities.ToAdd.OfType<Spikes>());
        bool changed = false;

        foreach (EntityData data in levelData.Entities)
        {
            if (!TryGetSpikeDirection(data.Name, out Spikes.Directions direction))
                continue;

            Vector2 position = data.Position + offset;
            if (existing.Any(spike => spike.Position == position && spike.Direction == direction))
                continue;

            Spikes spike = new(data, offset, direction);
            level.Add(spike);
            existing.Add(spike);
            changed = true;
        }

        return changed;
    }

    private static bool TryGetSpikeDirection(string entityName, out Spikes.Directions direction)
    {
        direction = entityName switch
        {
            "spikesUp" => Spikes.Directions.Up,
            "spikesDown" => Spikes.Directions.Down,
            "spikesLeft" => Spikes.Directions.Left,
            "spikesRight" => Spikes.Directions.Right,
            _ => default,
        };
        return entityName is "spikesUp" or "spikesDown" or "spikesLeft" or "spikesRight";
    }

    private static FinalBossMovingBlock? Find(Level level, int id)
        => level.Entities.OfType<FinalBossMovingBlock>().FirstOrDefault(block =>
            WatchEntityIDTable<FinalBossMovingBlock>.TryGet(
                block,
                level.Session.Level,
                out int candidate
            ) && candidate == id
        );

    private static void Block_ctor(
        On.Celeste.FinalBossMovingBlock.orig_ctor_EntityData_Vector2 orig,
        FinalBossMovingBlock self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FinalBossMovingBlock>.Set(self, data.Level.Name, data.ID);
    }

    private static void Block_StartMoving(
        On.Celeste.FinalBossMovingBlock.orig_StartMoving orig,
        FinalBossMovingBlock self,
        float delay
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self, delay);
        Publish(self, StartEvent);
    }

    private static IEnumerator Block_MoveSequence(
        On.Celeste.FinalBossMovingBlock.orig_MoveSequence orig,
        FinalBossMovingBlock self
    ) => MiaoNetModule.IsWatching ? EmptyRoutine() : orig(self);

    private static IEnumerator EmptyRoutine()
    {
        yield break;
    }

    private static void Block_Destroy(
        On.Celeste.FinalBossMovingBlock.orig_Destroy orig,
        FinalBossMovingBlock self,
        float delay
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, delay);
    }

    private static void Block_Finish(
        On.Celeste.FinalBossMovingBlock.orig_Finish orig,
        FinalBossMovingBlock self
    )
    {
        if (MiaoNetModule.IsWatching)
        {
            if (remoteFinishDepth > 0)
                orig(self);
            return;
        }
        Publish(self, BreakEvent);
        orig(self);
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        MaintainRemoteStates(self);
    }

    private static void Publish(FinalBossMovingBlock block, byte eventID)
    {
        if (block.Scene is Level level
            && WatchEntityIDTable<FinalBossMovingBlock>.TryGet(
                block,
                level.Session.Level,
                out int id
            ))
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(
                    new WatchEntityKey(WatchEntityKind.FinalBossMovingBlock, id),
                    eventID,
                    []
                )
            );
    }
}
