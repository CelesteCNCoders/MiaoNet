using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchPeriodicPlatformAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte MovingPlatformType = 0;
    private const byte SliderType = 1;
    private const byte TrackSpinnerType = 2;
    private const byte RotateSpinnerType = 3;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte Bool0Flag = 1 << 2;
    private const byte Bool1Flag = 1 << 3;
    private const byte Bool2Flag = 1 << 4;
    private const float PeriodicAnchorInterval = 0.1f;
    private const float PeriodicCorrectionFactor = 0.35f;
    private const float PeriodicHardPositionError = 12f;
    private const float SpinnerHardPhaseError = 0.2f;

    private readonly record struct PlatformState(
        byte Type,
        byte Flags,
        byte Aux,
        Vector2 Position,
        float Value0,
        float Value1,
        float Value2
    );

    private static readonly WatchPeriodicPlatformAdapter instance = new();
    private static readonly Dictionary<int, PlatformState> remoteStates = new();
    private static readonly ConditionalWeakTable<Entity, PeriodicSyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<Entity, RemoteApplyInfo> remoteApplyInfo = new();
    private static string? remoteRoom;

    private sealed class PeriodicSyncInfo
    {
        private bool hasState;
        private byte signatureFlags;
        private byte aux;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(
            int id,
            PlatformState current,
            bool forceCurrent,
            bool continuousAnchor,
            float sceneTime
        )
        {
            byte currentSignature = GetSyncSignature(current);
            if (forceCurrent
                || !hasState
                || signatureFlags != currentSignature
                || aux != current.Aux
                || (continuousAnchor && sceneTime >= nextAnchorTime))
            {
                state = Encode(id, current);
                signatureFlags = currentSignature;
                aux = current.Aux;
                hasState = true;
                if (continuousAnchor)
                    nextAnchorTime = sceneTime + PeriodicAnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteApplyInfo
    {
        public bool HasState { get; set; }
        public PlatformState State { get; set; }
    }

    public WatchEntityKind Kind => WatchEntityKind.PeriodicPlatform;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.MovingPlatform.ctor_Vector2_int_Vector2 += MovingPlatform_ctor_Vector2;
        On.Celeste.MovingPlatform.ctor_EntityData_Vector2 += MovingPlatform_ctor_EntityData;
        On.Celeste.Slider.ctor_EntityData_Vector2 += Slider_ctor;
        On.Celeste.Slider.Update += Slider_Update;
        On.Celeste.Slider.OnPlayer += Slider_OnPlayer;
        On.Celeste.TrackSpinner.ctor += TrackSpinner_ctor;
        On.Celeste.TrackSpinner.Update += TrackSpinner_Update;
        On.Celeste.TrackSpinner.OnPlayer += TrackSpinner_OnPlayer;
        On.Celeste.DustTrackSpinner.OnPlayer += DustTrackSpinner_OnPlayer;
        On.Celeste.RotateSpinner.ctor += RotateSpinner_ctor;
        On.Celeste.RotateSpinner.Update += RotateSpinner_Update;
        On.Celeste.RotateSpinner.OnPlayer += RotateSpinner_OnPlayer;
        On.Celeste.DustRotateSpinner.OnPlayer += DustRotateSpinner_OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.DustRotateSpinner.OnPlayer -= DustRotateSpinner_OnPlayer;
        On.Celeste.RotateSpinner.OnPlayer -= RotateSpinner_OnPlayer;
        On.Celeste.RotateSpinner.Update -= RotateSpinner_Update;
        On.Celeste.RotateSpinner.ctor -= RotateSpinner_ctor;
        On.Celeste.DustTrackSpinner.OnPlayer -= DustTrackSpinner_OnPlayer;
        On.Celeste.TrackSpinner.OnPlayer -= TrackSpinner_OnPlayer;
        On.Celeste.TrackSpinner.Update -= TrackSpinner_Update;
        On.Celeste.TrackSpinner.ctor -= TrackSpinner_ctor;
        On.Celeste.Slider.OnPlayer -= Slider_OnPlayer;
        On.Celeste.Slider.Update -= Slider_Update;
        On.Celeste.Slider.ctor_EntityData_Vector2 -= Slider_ctor;
        On.Celeste.MovingPlatform.ctor_EntityData_Vector2 -= MovingPlatform_ctor_EntityData;
        On.Celeste.MovingPlatform.ctor_Vector2_int_Vector2 -= MovingPlatform_ctor_Vector2;
        On.Celeste.Level.Update -= Level_Update;
        WatchEntityIDTable<MovingPlatform>.Clear();
        WatchSyntheticEntityIDTable<MovingPlatform>.Clear();
        WatchEntityIDTable<Slider>.Clear();
        WatchEntityIDTable<TrackSpinner>.Clear();
        WatchEntityIDTable<RotateSpinner>.Clear();
        syncInfo.Clear();
        remoteApplyInfo.Clear();
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach ((Entity entity, byte type, int id) in Enumerate(level))
        {
            PlatformState current = Capture(entity, type);
            yield return type != MovingPlatformType
                ? syncInfo.GetValue(entity, static _ => new()).Capture(
                    id,
                    current,
                    WatchEntitySyncRegistry.IsCapturingCurrentState,
                    type is SliderType or TrackSpinnerType or RotateSpinnerType,
                    level.TimeActive
                )
                : Encode(id, current);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        string room = level.Session.Level;
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteStates.Clear();
            remoteRoom = room;
        }

        HashSet<int> packetIDs = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || !TryDecode(state.Payload.Span, out PlatformState desired)
                || !packetIDs.Add(state.Key.EntityID))
                return WatchEntityApplyResult.None;
            remoteStates[state.Key.EntityID] = desired;
        }

        bool changed = false;
        bool requiresReload = false;
        HashSet<int> found = new();
        foreach ((Entity entity, byte type, int id) in Enumerate(level).ToArray())
        {
            found.Add(id);
            if (!remoteStates.TryGetValue(id, out PlatformState desired))
            {
                if (isCompleteState)
                {
                    entity.RemoveSelf();
                    changed = true;
                }
                continue;
            }

            if (desired.Type != type)
            {
                requiresReload = true;
                continue;
            }

            RemoteApplyInfo applied = remoteApplyInfo.GetValue(entity, static _ => new());
            // A death/respawn lifecycle must reset a locally simulated spinner
            // even when its new authoritative payload equals the initial anchor.
            // Ordinary complete states keep the cache guard so unrelated entity
            // changes cannot repeatedly restart periodic motion.
            if (type == MovingPlatformType
                || WatchEntitySyncRegistry.IsApplyingLifecycleReset
                || !applied.HasState)
            {
                changed |= Apply(entity, desired);
                applied.State = desired;
                applied.HasState = true;
            }
            else if (applied.State != desired)
            {
                changed |= type is SliderType or TrackSpinnerType or RotateSpinnerType
                    ? ApplyPeriodicCorrection(entity, desired)
                    : Apply(entity, desired);
                applied.State = desired;
            }
        }

        if (remoteStates.Keys.Any(id => !found.Contains(id)))
            requiresReload = true;

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }


    private static PlatformState Capture(Entity entity, byte type)
    {
        byte flags = 0;
        if (entity.Visible)
            flags |= VisibleFlag;
        if (entity.Collidable)
            flags |= CollidableFlag;

        return entity switch
        {
            MovingPlatform platform => new(
                type,
                flags,
                0,
                platform.Position,
                platform.addY,
                platform.sinkTimer,
                0f
            ),
            Slider slider => new(
                type,
                (byte)(flags
                    | (slider.moving ? Bool0Flag : 0)
                    | (slider.foundSurfaceAfterCorner ? Bool1Flag : 0)
                    | (slider.gotOutOfWall ? Bool2Flag : 0)),
                EncodeDirection(slider.dir),
                slider.Position,
                slider.speed,
                slider.surface.X,
                slider.surface.Y
            ),
            TrackSpinner spinner => new(
                type,
                (byte)(flags
                    | (spinner.Moving ? Bool0Flag : 0)
                    | (spinner.Up ? Bool1Flag : 0)),
                (byte)spinner.Speed,
                spinner.Position,
                spinner.Percent,
                spinner.PauseTimer,
                spinner.Angle
            ),
            RotateSpinner spinner => new(
                type,
                (byte)(flags
                    | (spinner.Moving ? Bool0Flag : 0)
                    | (spinner.Clockwise ? Bool1Flag : 0)),
                0,
                spinner.Position,
                spinner.rotationPercent,
                spinner.center.X,
                spinner.center.Y
            ),
            _ => default,
        };
    }

    private static byte GetSyncSignature(PlatformState state)
        => state.Type switch
        {
            SliderType => (byte)(state.Flags & (VisibleFlag | CollidableFlag | Bool0Flag)),
            TrackSpinnerType => (byte)(state.Flags & (VisibleFlag | CollidableFlag | Bool0Flag)),
            RotateSpinnerType => (byte)(state.Flags
                & (VisibleFlag | CollidableFlag | Bool0Flag | Bool1Flag)),
            _ => state.Flags,
        };

    private static bool Apply(Entity entity, PlatformState desired)
    {
        bool visible = (desired.Flags & VisibleFlag) != 0;
        bool collidable = (desired.Flags & CollidableFlag) != 0;
        bool changed = entity.Position != desired.Position
            || entity.Visible != visible
            || entity.Collidable != collidable;

        if (entity is Platform platform && platform.Position != desired.Position)
        {
            Vector2 movement = desired.Position - platform.Position;
            platform.Position = desired.Position;
            platform.MoveStaticMovers(movement);
            platform.ClearRemainder();
        }
        else
            entity.Position = desired.Position;

        entity.Visible = visible;
        entity.Collidable = collidable;
        switch (entity)
        {
            case MovingPlatform moving:
                changed |= moving.addY != desired.Value0 || moving.sinkTimer != desired.Value1;
                moving.addY = desired.Value0;
                moving.sinkTimer = desired.Value1;
                break;
            case Slider slider:
                bool movingValue = (desired.Flags & Bool0Flag) != 0;
                bool foundSurface = (desired.Flags & Bool1Flag) != 0;
                bool gotOut = (desired.Flags & Bool2Flag) != 0;
                Vector2 surface = new(desired.Value1, desired.Value2);
                changed |= slider.speed != desired.Value0
                    || slider.moving != movingValue
                    || slider.foundSurfaceAfterCorner != foundSurface
                    || slider.gotOutOfWall != gotOut
                    || slider.surface != surface
                    || slider.dir != DecodeDirection(desired.Aux);
                slider.speed = desired.Value0;
                slider.moving = movingValue;
                slider.foundSurfaceAfterCorner = foundSurface;
                slider.gotOutOfWall = gotOut;
                slider.surface = surface;
                slider.dir = DecodeDirection(desired.Aux);
                break;
            case TrackSpinner spinner:
                bool trackMoving = (desired.Flags & Bool0Flag) != 0;
                bool trackUp = (desired.Flags & Bool1Flag) != 0;
                changed |= spinner.Moving != trackMoving
                    || spinner.Up != trackUp
                    || (byte)spinner.Speed != desired.Aux
                    || spinner.Percent != desired.Value0
                    || spinner.PauseTimer != desired.Value1
                    || spinner.Angle != desired.Value2;
                spinner.Moving = trackMoving;
                spinner.Up = trackUp;
                spinner.Speed = (TrackSpinner.Speeds)desired.Aux;
                spinner.Percent = desired.Value0;
                spinner.PauseTimer = desired.Value1;
                spinner.Angle = desired.Value2;
                break;
            case RotateSpinner spinner:
                bool rotateMoving = (desired.Flags & Bool0Flag) != 0;
                bool clockwise = (desired.Flags & Bool1Flag) != 0;
                Vector2 center = new(desired.Value1, desired.Value2);
                changed |= spinner.Moving != rotateMoving
                    || spinner.Clockwise != clockwise
                    || spinner.rotationPercent != desired.Value0
                    || spinner.center != center;
                spinner.Moving = rotateMoving;
                spinner.Clockwise = clockwise;
                spinner.rotationPercent = desired.Value0;
                spinner.center = center;
                break;
        }
        return changed;
    }

    private static bool ApplyPeriodicCorrection(Entity entity, PlatformState desired)
    {
        bool visible = (desired.Flags & VisibleFlag) != 0;
        bool collidable = (desired.Flags & CollidableFlag) != 0;
        float positionError = Vector2.Distance(entity.Position, desired.Position);

        switch (entity)
        {
            case Slider slider:
            {
                bool moving = (desired.Flags & Bool0Flag) != 0;
                bool foundSurface = (desired.Flags & Bool1Flag) != 0;
                bool gotOut = (desired.Flags & Bool2Flag) != 0;
                Vector2 surface = new(desired.Value1, desired.Value2);
                Vector2 direction = DecodeDirection(desired.Aux);
                if (slider.moving != moving
                    || slider.foundSurfaceAfterCorner != foundSurface
                    || slider.gotOutOfWall != gotOut
                    || slider.surface != surface
                    || slider.dir != direction
                    || positionError >= PeriodicHardPositionError)
                    return Apply(entity, desired);

                Vector2 position = Vector2.Lerp(
                    slider.Position,
                    desired.Position,
                    PeriodicCorrectionFactor
                );
                float speed = MathHelper.Lerp(
                    slider.speed,
                    desired.Value0,
                    PeriodicCorrectionFactor
                );
                bool changed = slider.Position != position
                    || slider.speed != speed
                    || slider.Visible != visible
                    || slider.Collidable != collidable;
                slider.Position = position;
                slider.speed = speed;
                slider.Visible = visible;
                slider.Collidable = collidable;
                return changed;
            }
            case TrackSpinner spinner:
            {
                bool moving = (desired.Flags & Bool0Flag) != 0;
                bool up = (desired.Flags & Bool1Flag) != 0;
                float phaseError = Math.Abs(spinner.Percent - desired.Value0);
                if (spinner.Moving != moving
                    || spinner.Up != up
                    || (byte)spinner.Speed != desired.Aux
                    || positionError >= PeriodicHardPositionError
                    || phaseError >= SpinnerHardPhaseError)
                    return Apply(entity, desired);

                Vector2 position = Vector2.Lerp(
                    spinner.Position,
                    desired.Position,
                    PeriodicCorrectionFactor
                );
                float percent = MathHelper.Lerp(
                    spinner.Percent,
                    desired.Value0,
                    PeriodicCorrectionFactor
                );
                float pauseTimer = MathHelper.Lerp(
                    spinner.PauseTimer,
                    desired.Value1,
                    PeriodicCorrectionFactor
                );
                float angleDelta = MathHelper.WrapAngle(desired.Value2 - spinner.Angle);
                float angle = spinner.Angle + angleDelta * PeriodicCorrectionFactor;
                bool changed = spinner.Position != position
                    || spinner.Percent != percent
                    || spinner.PauseTimer != pauseTimer
                    || spinner.Angle != angle
                    || spinner.Visible != visible
                    || spinner.Collidable != collidable;
                spinner.Position = position;
                spinner.Percent = percent;
                spinner.PauseTimer = pauseTimer;
                spinner.Angle = angle;
                spinner.Visible = visible;
                spinner.Collidable = collidable;
                return changed;
            }
            case RotateSpinner spinner:
            {
                bool moving = (desired.Flags & Bool0Flag) != 0;
                bool clockwise = (desired.Flags & Bool1Flag) != 0;
                Vector2 center = new(desired.Value1, desired.Value2);
                float phaseDelta = WrapUnitDelta(desired.Value0 - spinner.rotationPercent);
                if (spinner.Moving != moving
                    || spinner.Clockwise != clockwise
                    || spinner.center != center
                    || positionError >= PeriodicHardPositionError
                    || Math.Abs(phaseDelta) >= SpinnerHardPhaseError)
                    return Apply(entity, desired);

                Vector2 position = Vector2.Lerp(
                    spinner.Position,
                    desired.Position,
                    PeriodicCorrectionFactor
                );
                float rotationPercent = WrapUnit(
                    spinner.rotationPercent + phaseDelta * PeriodicCorrectionFactor
                );
                bool changed = spinner.Position != position
                    || spinner.rotationPercent != rotationPercent
                    || spinner.Visible != visible
                    || spinner.Collidable != collidable;
                spinner.Position = position;
                spinner.rotationPercent = rotationPercent;
                spinner.Visible = visible;
                spinner.Collidable = collidable;
                return changed;
            }
            default:
                return Apply(entity, desired);
        }
    }

    private static float WrapUnitDelta(float value)
        => value - MathF.Floor(value + 0.5f);

    private static float WrapUnit(float value)
        => value - MathF.Floor(value);

    private static byte EncodeDirection(Vector2 direction)
        => direction switch
        {
            { X: 1f, Y: 0f } => 0,
            { X: -1f, Y: 0f } => 1,
            { X: 0f, Y: -1f } => 2,
            { X: 0f, Y: 1f } => 3,
            _ => 0,
        };

    private static Vector2 DecodeDirection(byte direction)
        => direction switch
        {
            0 => Vector2.UnitX,
            1 => -Vector2.UnitX,
            2 => -Vector2.UnitY,
            3 => Vector2.UnitY,
            _ => Vector2.UnitX,
        };

    private static IEnumerable<(Entity Entity, byte Type, int ID)> Enumerate(Level level)
    {
        string room = level.Session.Level;
        foreach (MovingPlatform platform in WatchRoomEntityIndex.Enumerate<MovingPlatform>(level))
        {
            if (WatchEntityIDTable<MovingPlatform>.TryGet(platform, room, out int id)
                || WatchSyntheticEntityIDTable<MovingPlatform>.TryGet(platform, out id))
                yield return (platform, MovingPlatformType, id);
        }
        foreach (Slider slider in WatchRoomEntityIndex.Enumerate<Slider>(level))
        {
            if (WatchEntityIDTable<Slider>.TryGet(slider, room, out int id))
                yield return (slider, SliderType, id);
        }
        foreach (TrackSpinner spinner in WatchRoomEntityIndex.Enumerate<TrackSpinner>(level))
        {
            if (WatchEntityIDTable<TrackSpinner>.TryGet(spinner, room, out int id))
                yield return (spinner, TrackSpinnerType, id);
        }
        foreach (RotateSpinner spinner in WatchRoomEntityIndex.Enumerate<RotateSpinner>(level))
        {
            if (WatchEntityIDTable<RotateSpinner>.TryGet(spinner, room, out int id))
                yield return (spinner, RotateSpinnerType, id);
        }
    }

    private static WatchEntityState Encode(int id, PlatformState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.PeriodicPlatform, id),
            state,
            PayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Type;
                payload[1] = value.Flags;
                payload[2] = value.Aux;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteSingle(payload, 12, value.Value0);
                WatchEntityPayloadCodec.WriteSingle(payload, 16, value.Value1);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, value.Value2);
            }
        );

    private static bool TryDecode(ReadOnlySpan<byte> payload, out PlatformState state)
    {
        state = default;
        if (payload.Length != PayloadSize
            || payload[0] > RotateSpinnerType
            || payload[3] != 0)
            return false;
        bool validHeader = payload[0] switch
        {
            MovingPlatformType => (payload[1] & ~(VisibleFlag | CollidableFlag)) == 0
                && payload[2] == 0,
            SliderType => (payload[1] & ~(VisibleFlag | CollidableFlag | Bool0Flag | Bool1Flag | Bool2Flag)) == 0
                && payload[2] <= 3,
            TrackSpinnerType => (payload[1] & ~(VisibleFlag | CollidableFlag | Bool0Flag | Bool1Flag)) == 0
                && payload[2] <= 2,
            RotateSpinnerType => (payload[1] & ~(VisibleFlag | CollidableFlag | Bool0Flag | Bool1Flag)) == 0
                && payload[2] == 0,
            _ => false,
        };
        if (!validHeader)
            return false;
        float x = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        float y = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        float value0 = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float value1 = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float value2 = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(value0)
            || !float.IsFinite(value1) || !float.IsFinite(value2))
            return false;
        state = new(payload[0], payload[1], payload[2], new Vector2(x, y), value0, value1, value2);
        return true;
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if (!MiaoNetModule.IsWatching
            || !StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            return;
        foreach ((Entity entity, byte type, int id) in Enumerate(self))
        {
            if (type == MovingPlatformType
                && remoteStates.TryGetValue(id, out PlatformState desired)
                && desired.Type == type)
                Apply(entity, desired);
        }
    }

    private static void MovingPlatform_ctor_Vector2(
        On.Celeste.MovingPlatform.orig_ctor_Vector2_int_Vector2 orig,
        MovingPlatform self,
        Vector2 position,
        int width,
        Vector2 node
    )
    {
        orig(self, position, width, node);
        WatchSyntheticEntityIDTable<MovingPlatform>.Set(self, StableID(position, node, width));
    }

    private static void MovingPlatform_ctor_EntityData(
        On.Celeste.MovingPlatform.orig_ctor_EntityData_Vector2 orig,
        MovingPlatform self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<MovingPlatform>.Set(self, data.Level.Name, data.ID);
    }

    private static void Slider_ctor(
        On.Celeste.Slider.orig_ctor_EntityData_Vector2 orig,
        Slider self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Slider>.Set(self, data.Level.Name, data.ID);
    }

    private static void Slider_Update(On.Celeste.Slider.orig_Update orig, Slider self)
    {
        if (!MiaoNetModule.IsWatching || !MiaoNetModule.IsWatchedPlayerPaused)
            orig(self);
    }

    private static void Slider_OnPlayer(
        On.Celeste.Slider.orig_OnPlayer orig,
        Slider self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void TrackSpinner_ctor(
        On.Celeste.TrackSpinner.orig_ctor orig,
        TrackSpinner self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<TrackSpinner>.Set(self, data.Level.Name, data.ID);
    }

    private static void TrackSpinner_OnPlayer(
        On.Celeste.TrackSpinner.orig_OnPlayer orig,
        TrackSpinner self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void TrackSpinner_Update(
        On.Celeste.TrackSpinner.orig_Update orig,
        TrackSpinner self
    )
    {
        if (!MiaoNetModule.IsWatching || !MiaoNetModule.IsWatchedPlayerPaused)
            orig(self);
    }

    private static void RotateSpinner_ctor(
        On.Celeste.RotateSpinner.orig_ctor orig,
        RotateSpinner self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<RotateSpinner>.Set(self, data.Level.Name, data.ID);
    }

    private static void RotateSpinner_OnPlayer(
        On.Celeste.RotateSpinner.orig_OnPlayer orig,
        RotateSpinner self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void RotateSpinner_Update(
        On.Celeste.RotateSpinner.orig_Update orig,
        RotateSpinner self
    )
    {
        if (!MiaoNetModule.IsWatching || !MiaoNetModule.IsWatchedPlayerPaused)
            orig(self);
    }

    private static void DustTrackSpinner_OnPlayer(
        On.Celeste.DustTrackSpinner.orig_OnPlayer orig,
        DustTrackSpinner self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void DustRotateSpinner_OnPlayer(
        On.Celeste.DustRotateSpinner.orig_OnPlayer orig,
        DustRotateSpinner self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static int StableID(Vector2 start, Vector2 end, int width)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(start.X)) * 16777619;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(start.Y)) * 16777619;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(end.X)) * 16777619;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(end.Y)) * 16777619;
            hash = (hash ^ (uint)width) * 16777619;
            return 0x40000000 | (int)(hash & 0x3fffffff);
        }
    }
}
