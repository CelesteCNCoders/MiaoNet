using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchMovingSolidAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte Bool0Flag = 1 << 2;
    private const byte Bool1Flag = 1 << 3;
    private const byte Bool2Flag = 1 << 4;
    private const byte KnownFlags = VisibleFlag | CollidableFlag | Bool0Flag | Bool1Flag | Bool2Flag;
    private const byte FallingShakeEvent = 1;
    private const byte FallingImpactEvent = 2;
    private const byte FallingLandParticlesEvent = 3;

    private readonly record struct MovingSolidState(
        WatchMovingSolidType Type,
        byte Flags,
        byte State,
        Vector2 Position,
        float Value0,
        float Value1,
        float Value2
    )
    {
        public bool Visible => (Flags & VisibleFlag) != 0;
        public bool Collidable => (Flags & CollidableFlag) != 0;
        public bool Bool0 => (Flags & Bool0Flag) != 0;
        public bool Bool1 => (Flags & Bool1Flag) != 0;
        public bool Bool2 => (Flags & Bool2Flag) != 0;
    }

    private static readonly WatchMovingSolidAdapter instance = new();
    private static readonly Dictionary<int, MovingSolidState> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.MovingSolid;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.ZipMover.ctor_EntityData_Vector2 += ZipMover_ctor;
        On.Celeste.SwapBlock.ctor_EntityData_Vector2 += SwapBlock_ctor;
        On.Celeste.MoveBlock.ctor_EntityData_Vector2 += MoveBlock_ctor;
        On.Celeste.FallingBlock.ctor_EntityData_Vector2 += FallingBlock_ctor;
        On.Celeste.FallingBlock.CreateFinalBossBlock += FallingBlock_CreateFinalBossBlock;
        On.Celeste.FallingBlock.ShakeSfx += FallingBlock_ShakeSfx;
        On.Celeste.FallingBlock.ImpactSfx += FallingBlock_ImpactSfx;
        On.Celeste.FallingBlock.LandParticles += FallingBlock_LandParticles;
        On.Celeste.CrushBlock.ctor_EntityData_Vector2 += CrushBlock_ctor;
        On.Celeste.SinkingPlatform.ctor_EntityData_Vector2 += SinkingPlatform_ctor;
        On.Celeste.FloatySpaceBlock.ctor_EntityData_Vector2 += FloatySpaceBlock_ctor;
        On.Celeste.DreamBlock.ctor_EntityData_Vector2 += DreamBlock_ctor;
        On.Celeste.GoldenBlock.ctor_EntityData_Vector2 += GoldenBlock_ctor;
        On.Celeste.GlassBlock.ctor_EntityData_Vector2 += GlassBlock_ctor;
        On.Celeste.StarJumpBlock.ctor_EntityData_Vector2 += StarJumpBlock_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.StarJumpBlock.ctor_EntityData_Vector2 -= StarJumpBlock_ctor;
        On.Celeste.GlassBlock.ctor_EntityData_Vector2 -= GlassBlock_ctor;
        On.Celeste.GoldenBlock.ctor_EntityData_Vector2 -= GoldenBlock_ctor;
        On.Celeste.DreamBlock.ctor_EntityData_Vector2 -= DreamBlock_ctor;
        On.Celeste.FloatySpaceBlock.ctor_EntityData_Vector2 -= FloatySpaceBlock_ctor;
        On.Celeste.SinkingPlatform.ctor_EntityData_Vector2 -= SinkingPlatform_ctor;
        On.Celeste.CrushBlock.ctor_EntityData_Vector2 -= CrushBlock_ctor;
        On.Celeste.FallingBlock.LandParticles -= FallingBlock_LandParticles;
        On.Celeste.FallingBlock.ImpactSfx -= FallingBlock_ImpactSfx;
        On.Celeste.FallingBlock.ShakeSfx -= FallingBlock_ShakeSfx;
        On.Celeste.FallingBlock.CreateFinalBossBlock -= FallingBlock_CreateFinalBossBlock;
        On.Celeste.FallingBlock.ctor_EntityData_Vector2 -= FallingBlock_ctor;
        On.Celeste.MoveBlock.ctor_EntityData_Vector2 -= MoveBlock_ctor;
        On.Celeste.SwapBlock.ctor_EntityData_Vector2 -= SwapBlock_ctor;
        On.Celeste.ZipMover.ctor_EntityData_Vector2 -= ZipMover_ctor;
        On.Celeste.Level.Update -= Level_Update;

        WatchEntityIDTable<ZipMover>.Clear();
        WatchEntityIDTable<SwapBlock>.Clear();
        WatchEntityIDTable<MoveBlock>.Clear();
        WatchEntityIDTable<FallingBlock>.Clear();
        WatchEntityIDTable<CrushBlock>.Clear();
        WatchEntityIDTable<SinkingPlatform>.Clear();
        WatchEntityIDTable<FloatySpaceBlock>.Clear();
        WatchEntityIDTable<DreamBlock>.Clear();
        WatchEntityIDTable<GoldenBlock>.Clear();
        WatchEntityIDTable<GlassBlock>.Clear();
        WatchEntityIDTable<StarJumpBlock>.Clear();
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach ((Entity entity, WatchMovingSolidType type, int id) in EnumerateTracked(level))
            yield return Encode(id, Capture(entity, type));
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, MovingSolidState> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || !TryDecode(state.Payload.Span, out MovingSolidState desired)
                || !desiredByID.TryAdd(state.Key.EntityID, desired))
                return WatchEntityApplyResult.None;
        }

        string room = level.Session.Level;
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteStates.Clear();
            remoteRoom = room;
        }
        foreach ((int id, MovingSolidState desired) in desiredByID)
            remoteStates[id] = desired;

        bool changed = false;
        bool requiresReload = false;
        foreach ((Entity entity, WatchMovingSolidType type, int id) in EnumerateTracked(level).ToArray())
        {
            if (!desiredByID.Remove(id, out MovingSolidState desired))
            {
                if (!isCompleteState)
                    continue;

                entity.RemoveSelf();
                changed = true;
                continue;
            }

            if (desired.Type != type)
            {
                requiresReload = true;
                continue;
            }

            if (entity is FallingBlock falling
                && falling.HasStartedFalling
                && !desired.Bool1)
            {
                requiresReload = true;
                continue;
            }

            changed |= Apply(entity, desired, replayTransitions: true);
        }

        if (desiredByID.Count > 0)
            requiresReload = true;

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.Key.SubID != 0 || entityEvent.Payload.Length != 0)
            return;
        FallingBlock? block = level.Entities.OfType<FallingBlock>().FirstOrDefault(candidate =>
            WatchEntityIDTable<FallingBlock>.TryGet(
                candidate,
                level.Session.Level,
                out int id
            ) && id == entityEvent.Key.EntityID
        );
        if (block is null)
            return;
        switch (entityEvent.EventID)
        {
            case FallingShakeEvent:
                block.ShakeSfx();
                break;
            case FallingImpactEvent:
                block.ImpactSfx();
                break;
            case FallingLandParticlesEvent:
                block.LandParticles();
                break;
        }
    }

    private static MovingSolidState Capture(Entity entity, WatchMovingSolidType type)
    {
        byte flags = 0;
        if (entity.Visible)
            flags |= VisibleFlag;
        if (entity.Collidable)
            flags |= CollidableFlag;

        byte state = 0;
        float value0 = 0f;
        float value1 = 0f;
        float value2 = 0f;
        switch (entity)
        {
            case ZipMover mover:
                value0 = mover.percent;
                break;
            case SwapBlock block:
                if (block.Swapping)
                    flags |= Bool0Flag;
                state = (byte)Math.Clamp(block.target, 0, 1);
                value0 = block.lerp;
                value1 = block.speed;
                value2 = block.returnTimer;
                break;
            case MoveBlock block:
                if (block.triggered)
                    flags |= Bool0Flag;
                state = (byte)block.state;
                value0 = block.speed;
                value1 = block.angle;
                value2 = block.targetAngle;
                break;
            case FallingBlock block:
                if (block.Triggered)
                    flags |= Bool0Flag;
                if (block.HasStartedFalling)
                    flags |= Bool1Flag;
                if (block.finalBoss)
                    flags |= Bool2Flag;
                value0 = block.FallDelay;
                break;
            case CrushBlock block:
                if (block.canActivate)
                    flags |= Bool0Flag;
                if (block.crushDir != Vector2.Zero)
                    flags |= Bool1Flag;
                value0 = block.crushDir.X;
                value1 = block.crushDir.Y;
                break;
            case SinkingPlatform platform:
                value0 = platform.speed;
                value1 = platform.riseTimer;
                value2 = platform.startY;
                break;
            case FloatySpaceBlock block:
                if (block.awake)
                    flags |= Bool0Flag;
                value0 = block.yLerp;
                value1 = block.sinkTimer;
                value2 = block.dashEase;
                break;
            case DreamBlock block:
                if (block.playerHasDreamDash)
                    flags |= Bool0Flag;
                if (block.oneUse)
                    flags |= Bool1Flag;
                value0 = block.whiteFill;
                // DreamBlock's decorative particle and border animation is
                // deterministic local presentation. Sending its continuously
                // advancing phase made the Watcher run the original Update and
                // then snap back to the latest network sample every frame.
                // Keep the two spare values stable so the local visual clock is
                // the only authority and does not create per-frame scene deltas.
                break;
            case GoldenBlock block:
                if (block.berry.Visible)
                    flags |= Bool0Flag;
                value0 = block.yLerp;
                value1 = block.sinkTimer;
                value2 = block.renderLerp;
                break;
            case GlassBlock block:
                if (block.sinks)
                    flags |= Bool0Flag;
                break;
            case StarJumpBlock block:
                if (block.sinks)
                    flags |= Bool0Flag;
                value0 = block.yLerp;
                value1 = block.sinkTimer;
                value2 = block.startY;
                break;
        }

        return new MovingSolidState(type, flags, state, entity.Position, value0, value1, value2);
    }

    private static bool Apply(
        Entity entity,
        MovingSolidState desired,
        bool replayTransitions
    )
    {
        MovingSolidState current = Capture(entity, desired.Type);
        if (current == desired)
            return entity is FloatySpaceBlock currentFloaty
                && MaintainFloatyJumpThrus(currentFloaty, desired.Position);

        bool positionChanged = entity.Position != desired.Position;
        if (positionChanged && entity is Platform platform)
        {
            // Remote correction must travel through Platform movement so riders are
            // carried with it. Direct Position assignment leaves Actors behind and
            // makes them saw back to their own authoritative position on the next patch.
            platform.MoveTo(desired.Position);
            platform.ClearRemainder();
        }
        else if (positionChanged)
        {
            entity.Position = desired.Position;
        }
        entity.Visible = desired.Visible;
        entity.Collidable = desired.Collidable;

        switch (entity)
        {
            case ZipMover mover:
                mover.percent = desired.Value0;
                break;
            case SwapBlock block:
                if (replayTransitions && !current.Bool0 && desired.Bool0)
                    block.OnDash(Vector2.Zero);
                block.Swapping = desired.Bool0;
                block.target = desired.State;
                block.lerp = desired.Value0;
                block.speed = desired.Value1;
                block.returnTimer = desired.Value2;
                break;
            case MoveBlock block:
                block.triggered = desired.Bool0;
                block.state = (MoveBlock.MovementState)desired.State;
                block.speed = desired.Value0;
                block.angle = desired.Value1;
                block.targetAngle = desired.Value2;
                block.UpdateColors();
                break;
            case FallingBlock block:
                if (replayTransitions && !current.Bool0 && desired.Bool0)
                    block.StartShaking(0.5f);
                block.Triggered = desired.Bool0;
                block.HasStartedFalling = desired.Bool1;
                block.FallDelay = desired.Value0;
                break;
            case CrushBlock block:
                Vector2 direction = new(desired.Value0, desired.Value1);
                if (replayTransitions
                    && block.crushDir == Vector2.Zero
                    && direction != Vector2.Zero)
                    block.Attack(direction);
                block.crushDir = direction;
                block.canActivate = desired.Bool0;
                break;
            case SinkingPlatform sinkingPlatform:
                sinkingPlatform.speed = desired.Value0;
                sinkingPlatform.riseTimer = desired.Value1;
                sinkingPlatform.startY = desired.Value2;
                break;
            case FloatySpaceBlock block:
                block.awake = desired.Bool0;
                block.yLerp = desired.Value0;
                block.sinkTimer = desired.Value1;
                block.dashEase = desired.Value2;
                MaintainFloatyJumpThrus(block, desired.Position);
                break;
            case DreamBlock block:
                if (block.playerHasDreamDash != desired.Bool0)
                {
                    if (desired.Bool0)
                        block.ActivateNoRoutine();
                    else
                        block.DeactivateNoRoutine();
                }
                block.playerHasDreamDash = desired.Bool0;
                block.whiteFill = desired.Value0;
                break;
            case GoldenBlock block:
                block.berry.Visible = desired.Bool0;
                block.yLerp = desired.Value0;
                block.sinkTimer = desired.Value1;
                block.renderLerp = desired.Value2;
                break;
            case GlassBlock:
                break;
            case StarJumpBlock block:
                block.yLerp = desired.Value0;
                block.sinkTimer = desired.Value1;
                block.startY = desired.Value2;
                break;
        }
        return true;
    }

    private static bool MaintainFloatyJumpThrus(
        FloatySpaceBlock block,
        Vector2 remotePosition
    )
    {
        if (block.Scene is not Level level)
            return false;

        FloatySpaceBlock? master = block.MasterOfGroup
            ? block
            : level.Entities.OfType<FloatySpaceBlock>().FirstOrDefault(candidate =>
                candidate.MasterOfGroup
                && candidate.Group is not null
                && candidate.Group.Contains(block)
            );
        if (master?.Moves is null
            || master.Jumpthrus is null
            || !master.Moves.TryGetValue(block, out Vector2 originalBlockPosition))
            return false;

        Vector2 displacement = remotePosition - originalBlockPosition;
        bool changed = false;
        foreach (JumpThru jumpThru in master.Jumpthrus)
        {
            if (jumpThru.Scene != level
                || !master.Moves.TryGetValue(jumpThru, out Vector2 originalJumpThruPosition))
                continue;

            Vector2 target = originalJumpThruPosition + displacement;
            if (jumpThru.X != target.X)
            {
                jumpThru.MoveToX(target.X);
                changed = true;
            }
            if (jumpThru.Y != target.Y)
            {
                jumpThru.MoveToY(target.Y);
                changed = true;
            }
        }
        return changed;
    }

    private static void MaintainRemoteStates(Level level)
    {
        if (!MiaoNetModule.IsWatching
            || !StringComparer.Ordinal.Equals(remoteRoom, level.Session.Level)
            || remoteStates.Count == 0)
            return;

        foreach ((Entity entity, WatchMovingSolidType type, int id) in EnumerateTracked(level))
        {
            if (remoteStates.TryGetValue(id, out MovingSolidState desired)
                && desired.Type == type)
                Apply(entity, desired, replayTransitions: false);
        }
    }

    private static WatchEntityState Encode(int id, MovingSolidState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = (byte)state.Type;
        payload[1] = state.Flags;
        payload[2] = state.State;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.Value0);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.Value1);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.Value2);
        return new WatchEntityState(new WatchEntityKey(WatchEntityKind.MovingSolid, id), payload);
    }

    private static bool TryDecode(ReadOnlySpan<byte> payload, out MovingSolidState state)
    {
        state = default;
        if (payload.Length != PayloadSize
            || payload[0] > (byte)WatchMovingSolidType.StarJumpBlock
            || (payload[1] & ~KnownFlags) != 0
            || payload[3] != 0)
            return false;

        WatchMovingSolidType type = (WatchMovingSolidType)payload[0];
        byte entityState = payload[2];
        if ((type == WatchMovingSolidType.SwapBlock && entityState > 1)
            || (type == WatchMovingSolidType.MoveBlock && entityState > 2)
            || (type is not (WatchMovingSolidType.SwapBlock
                or WatchMovingSolidType.MoveBlock) && entityState != 0)
            || type == WatchMovingSolidType.BounceBlock)
            return false;

        Vector2 position = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 4),
            WatchEntityPayloadCodec.ReadSingle(payload, 8)
        );
        float value0 = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float value1 = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float value2 = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(position.X)
            || !float.IsFinite(position.Y)
            || !float.IsFinite(value0)
            || !float.IsFinite(value1)
            || !float.IsFinite(value2))
            return false;

        state = new MovingSolidState(
            type,
            payload[1],
            entityState,
            position,
            value0,
            value1,
            value2
        );
        return true;
    }

    private static IEnumerable<(Entity Entity, WatchMovingSolidType Type, int ID)> EnumerateTracked(
        Level level
    )
    {
        string room = level.Session.Level;
        foreach (Entity entity in level.Entities)
        {
            WatchMovingSolidType type;
            int id;
            switch (entity)
            {
                case ZipMover value when WatchEntityIDTable<ZipMover>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.ZipMover;
                    break;
                case SwapBlock value when WatchEntityIDTable<SwapBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.SwapBlock;
                    break;
                case MoveBlock value when WatchEntityIDTable<MoveBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.MoveBlock;
                    break;
                case FallingBlock value when WatchEntityIDTable<FallingBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.FallingBlock;
                    break;
                case CrushBlock value when WatchEntityIDTable<CrushBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.CrushBlock;
                    break;
                case SinkingPlatform value when WatchEntityIDTable<SinkingPlatform>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.SinkingPlatform;
                    break;
                case FloatySpaceBlock value when WatchEntityIDTable<FloatySpaceBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.FloatySpaceBlock;
                    break;
                case DreamBlock value when WatchEntityIDTable<DreamBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.DreamBlock;
                    break;
                case GoldenBlock value when WatchEntityIDTable<GoldenBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.GoldenBlock;
                    break;
                case GlassBlock value when WatchEntityIDTable<GlassBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.GlassBlock;
                    break;
                case StarJumpBlock value when WatchEntityIDTable<StarJumpBlock>.TryGet(value, room, out id):
                    type = WatchMovingSolidType.StarJumpBlock;
                    break;
                default:
                    continue;
            }
            yield return (entity, type, id);
        }
    }

    private static void Track<TEntity>(TEntity entity, EntityData data) where TEntity : class
        => WatchEntityIDTable<TEntity>.Set(entity, data.Level.Name, data.ID);

    private static void FallingBlock_ShakeSfx(
        On.Celeste.FallingBlock.orig_ShakeSfx orig,
        FallingBlock self
    ) => ReplayFallingVisual(self, FallingShakeEvent, () => orig(self));

    private static void FallingBlock_ImpactSfx(
        On.Celeste.FallingBlock.orig_ImpactSfx orig,
        FallingBlock self
    ) => ReplayFallingVisual(self, FallingImpactEvent, () => orig(self));

    private static void FallingBlock_LandParticles(
        On.Celeste.FallingBlock.orig_LandParticles orig,
        FallingBlock self
    ) => ReplayFallingVisual(self, FallingLandParticlesEvent, () => orig(self));

    private static void ReplayFallingVisual(
        FallingBlock self,
        byte eventID,
        Action orig
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        if (!MiaoNetModule.IsWatching && self.Scene is Level level
            && WatchEntityIDTable<FallingBlock>.TryGet(
                self,
                level.Session.Level,
                out int id
            ))
        {
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.MovingSolid, id),
                eventID,
                []
            ));
        }
        orig();
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        MaintainRemoteStates(self);
    }

    private static void ZipMover_ctor(On.Celeste.ZipMover.orig_ctor_EntityData_Vector2 orig, ZipMover self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void SwapBlock_ctor(On.Celeste.SwapBlock.orig_ctor_EntityData_Vector2 orig, SwapBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void MoveBlock_ctor(On.Celeste.MoveBlock.orig_ctor_EntityData_Vector2 orig, MoveBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void FallingBlock_ctor(On.Celeste.FallingBlock.orig_ctor_EntityData_Vector2 orig, FallingBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static FallingBlock FallingBlock_CreateFinalBossBlock(On.Celeste.FallingBlock.orig_CreateFinalBossBlock orig, EntityData data, Vector2 offset)
    {
        FallingBlock block = orig(data, offset);
        Track(block, data);
        return block;
    }

    private static void CrushBlock_ctor(On.Celeste.CrushBlock.orig_ctor_EntityData_Vector2 orig, CrushBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void SinkingPlatform_ctor(On.Celeste.SinkingPlatform.orig_ctor_EntityData_Vector2 orig, SinkingPlatform self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void FloatySpaceBlock_ctor(On.Celeste.FloatySpaceBlock.orig_ctor_EntityData_Vector2 orig, FloatySpaceBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void DreamBlock_ctor(On.Celeste.DreamBlock.orig_ctor_EntityData_Vector2 orig, DreamBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void GoldenBlock_ctor(On.Celeste.GoldenBlock.orig_ctor_EntityData_Vector2 orig, GoldenBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void GlassBlock_ctor(On.Celeste.GlassBlock.orig_ctor_EntityData_Vector2 orig, GlassBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }

    private static void StarJumpBlock_ctor(On.Celeste.StarJumpBlock.orig_ctor_EntityData_Vector2 orig, StarJumpBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        Track(self, data);
    }
}
