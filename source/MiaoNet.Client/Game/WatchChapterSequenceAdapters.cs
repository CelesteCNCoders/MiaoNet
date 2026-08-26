using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchBadelineBoostAdapter : IWatchEntityAdapter
{
    private const byte ActivateEvent = 1;
    private const int PayloadSize = 16;
    private const byte EntityVisibleFlag = 1 << 0;
    private const byte TravellingFlag = 1 << 1;
    private const byte HoldingFlag = 1 << 2;
    private const byte SpriteVisibleFlag = 1 << 3;
    private const byte StretchVisibleFlag = 1 << 4;

    private sealed class Info
    {
        public string Level { get; }
        public int ID { get; }
        public Info(string level, int id) { Level = level; ID = id; }
    }

    private static readonly WatchBadelineBoostAdapter instance = new();
    private static readonly ConditionalWeakTable<BadelineBoost, Info> infos = new();

    public WatchEntityKind Kind => WatchEntityKind.BadelineBoost;

    public static void Load()
    {
        On.Celeste.BadelineBoost.ctor_EntityData_Vector2 += BadelineBoost_ctor;
        On.Celeste.BadelineBoost.OnPlayer += BadelineBoost_OnPlayer;
        On.Celeste.BadelineBoost.Update += BadelineBoost_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BadelineBoost.Update -= BadelineBoost_Update;
        On.Celeste.BadelineBoost.OnPlayer -= BadelineBoost_OnPlayer;
        On.Celeste.BadelineBoost.ctor_EntityData_Vector2 -= BadelineBoost_ctor;
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (BadelineBoost boost in level.Entities.OfType<BadelineBoost>().ToArray())
        {
            if (!infos.TryGetValue(boost, out Info? info) || info.Level != level.Session.Level)
                continue;
            WatchEntityPhase phase = boost.holding is not null
                ? WatchEntityPhase.Active
                : boost.travelling
                    ? WatchEntityPhase.Returning
                    : boost.Visible ? WatchEntityPhase.Ready : WatchEntityPhase.Gone;
            byte[] payload = new byte[PayloadSize];
            payload[0] = (byte)phase;
            if (boost.Visible) payload[1] |= EntityVisibleFlag;
            if (boost.travelling) payload[1] |= TravellingFlag;
            if (boost.holding is not null) payload[1] |= HoldingFlag;
            if (boost.sprite.Visible) payload[1] |= SpriteVisibleFlag;
            if (boost.stretch.Visible) payload[1] |= StretchVisibleFlag;
            WatchEntityPayloadCodec.WriteUInt16(payload, 2, checked((ushort)Math.Clamp(boost.nodeIndex, 0, ushort.MaxValue)));
            WatchEntityPayloadCodec.WriteSingle(payload, 4, boost.Position.X);
            WatchEntityPayloadCodec.WriteSingle(payload, 8, boost.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(payload, 12, GetStretchProgress(boost));
            yield return new(new WatchEntityKey(Kind, info.ID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (BadelineBoost boost in level.Entities.OfType<BadelineBoost>().ToArray())
        {
            if (!infos.TryGetValue(boost, out Info? info) || info.Level != level.Session.Level)
                continue;
            if (!desired.TryGetValue(info.ID, out WatchEntityState state))
            {
                if (isCompleteState)
                {
                    boost.RemoveSelf();
                    changed = true;
                }
                continue;
            }
            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase phase = (WatchEntityPhase)payload[0];
            boost.nodeIndex = Math.Min(
                WatchEntityPayloadCodec.ReadUInt16(payload, 2),
                Math.Max(0, boost.nodes.Length - 1)
            );
            boost.Position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 4),
                WatchEntityPayloadCodec.ReadSingle(payload, 8));
            boost.travelling = (payload[1] & TravellingFlag) != 0;
            boost.holding = null;
            boost.Visible = phase != WatchEntityPhase.Gone
                && (payload[1] & EntityVisibleFlag) != 0;
            boost.sprite.Visible = (payload[1] & SpriteVisibleFlag) != 0;
            ApplyStretchPresentation(
                boost,
                (payload[1] & StretchVisibleFlag) != 0,
                WatchEntityPayloadCodec.ReadSingle(payload, 12)
            );
            // The watched client is the only authority for activation. The
            // hidden transport Player must never start a local BoostRoutine.
            boost.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ActivateEvent || entityEvent.Payload.Length != 0)
            return;
        BadelineBoost? boost = level.Entities.OfType<BadelineBoost>().FirstOrDefault(candidate =>
            infos.TryGetValue(candidate, out Info? info)
            && info.Level == level.Session.Level && info.ID == entityEvent.Key.EntityID);
        if (boost is null)
            return;
        boost.Wiggle();
        boost.Collidable = false;
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.BadelineBoost && state.Key.SubID == 0
            && payload.Length == PayloadSize
            && payload[0] <= (byte)WatchEntityPhase.Returning
            && (payload[1] & ~0b0001_1111) == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 4))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && WatchEntityPayloadCodec.ReadSingle(payload, 12) is >= 0f and <= 1f;
    }

    private static float GetStretchProgress(BadelineBoost boost)
    {
        if (!boost.travelling || !boost.stretch.Visible
            || boost.nodeIndex <= 0 || boost.nodeIndex >= boost.nodes.Length)
            return 0f;

        Vector2 from = boost.nodes[boost.nodeIndex - 1];
        Vector2 delta = boost.nodes[boost.nodeIndex] - from;
        float lengthSquared = delta.LengthSquared();
        return lengthSquared <= 0.0001f
            ? 1f
            : Math.Clamp(Vector2.Dot(boost.Position - from, delta) / lengthSquared, 0f, 1f);
    }

    private static void ApplyStretchPresentation(
        BadelineBoost boost,
        bool visible,
        float easedProgress
    )
    {
        visible &= boost.nodeIndex > 0 && boost.nodeIndex < boost.nodes.Length;
        boost.stretch.Visible = visible;
        if (!visible)
            return;

        Vector2 from = boost.nodes[boost.nodeIndex - 1];
        Vector2 to = boost.nodes[boost.nodeIndex];
        float stretch = Calc.YoYo(easedProgress);
        boost.stretch.Rotation = Calc.Angle(to - from);
        boost.stretch.Scale.X = 1f + stretch * 2f;
        boost.stretch.Scale.Y = 1f - stretch * 0.75f;
    }

    private static void BadelineBoost_ctor(
        On.Celeste.BadelineBoost.orig_ctor_EntityData_Vector2 orig,
        BadelineBoost self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new Info(data.Level.Name, data.ID));
    }

    private static void BadelineBoost_OnPlayer(
        On.Celeste.BadelineBoost.orig_OnPlayer orig,
        BadelineBoost self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;

        bool wasAvailable = self.Collidable;
        orig(self, player);
        if (!wasAvailable || self.Collidable || WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level || !infos.TryGetValue(self, out Info? info))
            return;
        WatchEntitySyncRegistry.PublishEvent(level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.BadelineBoost, info.ID), ActivateEvent, []));
    }

    private static void BadelineBoost_Update(
        On.Celeste.BadelineBoost.orig_Update orig,
        BadelineBoost self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;

        // Preserve vanilla particles, light/bloom visibility and component
        // clocks, but force the Update branch that cannot query Player or call
        // Skip. Authoritative state restores the real travelling value.
        bool travelling = self.travelling;
        self.travelling = true;
        self.holding = null;
        try
        {
            orig(self);
        }
        finally
        {
            self.travelling = travelling;
            self.holding = null;
            self.Collidable = false;
        }
    }
}

internal sealed class WatchFlingBirdAdapter : IWatchEntityAdapter
{
    private const byte ActivateEvent = 1;
    private const int PayloadSize = 56;
    private const float AnchorInterval = 0.1f;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte LightningRemovedFlag = 1 << 2;

    private readonly record struct BirdState(
        byte State,
        byte Flags,
        ushort SegmentIndex,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 FlingSpeed,
        Vector2 FlingTargetSpeed,
        float FlingAccel,
        Vector2 SpritePosition,
        Vector2 SpriteScale,
        float SpriteRotation
    );

    private sealed class Info
    {
        public string Level { get; }
        public int ID { get; }
        public Info(string level, int id) { Level = level; ID = id; }
    }

    private sealed class SyncInfo
    {
        private bool hasState;
        private BirdState last;
        private float nextAnchor;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, BirdState current, float time, bool force)
        {
            bool signatureChanged = !hasState
                || last.State != current.State
                || last.Flags != current.Flags
                || last.SegmentIndex != current.SegmentIndex
                || last.Animation != current.Animation;
            if (force || signatureChanged || time >= nextAnchor)
            {
                state = Encode(id, current);
                last = current;
                hasState = true;
                nextAnchor = time + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public Vector2 PositionStart { get; set; }
        public Vector2 PositionTarget { get; set; }
        public Vector2 SpritePositionStart { get; set; }
        public Vector2 SpritePositionTarget { get; set; }
        public Vector2 SpriteScaleStart { get; set; }
        public Vector2 SpriteScaleTarget { get; set; }
        public float SpriteRotationStart { get; set; }
        public float SpriteRotationTarget { get; set; }
        public float Elapsed { get; set; }
        public bool HasAnimation { get; set; }
        public byte Animation { get; set; }
        public Vector2 LastTrail { get; set; }
    }

    private static readonly WatchFlingBirdAdapter instance = new();
    private static readonly ConditionalWeakTable<FlingBird, Info> infos = new();
    private static readonly ConditionalWeakTable<FlingBird, SyncInfo> syncInfos = new();
    private static readonly ConditionalWeakTable<FlingBird, RemoteInfo> remoteInfos = new();
    public WatchEntityKind Kind => WatchEntityKind.FlingBird;

    public static void Load()
    {
        On.Celeste.FlingBird.ctor_EntityData_Vector2 += FlingBird_ctor;
        On.Celeste.FlingBird.OnPlayer += FlingBird_OnPlayer;
        On.Celeste.FlingBird.Update += FlingBird_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FlingBird.Update -= FlingBird_Update;
        On.Celeste.FlingBird.OnPlayer -= FlingBird_OnPlayer;
        On.Celeste.FlingBird.ctor_EntityData_Vector2 -= FlingBird_ctor;
        infos.Clear();
        syncInfos.Clear();
        remoteInfos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (FlingBird bird in level.Entities.OfType<FlingBird>().ToArray())
        {
            if (!infos.TryGetValue(bird, out Info? info) || info.Level != level.Session.Level)
                continue;
            yield return syncInfos.GetValue(bird, static _ => new()).Capture(
                info.ID,
                Capture(bird),
                level.TimeActive,
                WatchEntitySyncRegistry.IsCapturingCurrentState
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (FlingBird bird in level.Entities.OfType<FlingBird>().ToArray())
        {
            if (!infos.TryGetValue(bird, out Info? info) || info.Level != level.Session.Level)
                continue;
            if (!desired.TryGetValue(info.ID, out WatchEntityState state))
            {
                if (isCompleteState)
                {
                    bird.RemoveSelf();
                    changed = true;
                }
                continue;
            }
            Apply(bird, Decode(state));
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ActivateEvent || entityEvent.Payload.Length != 0)
            return;
        FlingBird? bird = level.Entities.OfType<FlingBird>().FirstOrDefault(candidate =>
            infos.TryGetValue(candidate, out Info? info)
            && info.Level == level.Session.Level && info.ID == entityEvent.Key.EntityID);
        if (bird is null)
            return;
        Audio.Play("event:/new_content/game/10_farewell/bird_throw", bird.Position);
        bird.Collidable = false;
    }

    private static BirdState Capture(FlingBird bird)
    {
        byte flags = 0;
        if (bird.Visible) flags |= VisibleFlag;
        if (bird.Collidable) flags |= CollidableFlag;
        if (bird.LightningRemoved) flags |= LightningRemovedFlag;
        byte animation = bird.sprite?.CurrentAnimationID switch
        {
            "hover" => 0,
            "hoverStressed" => 1,
            "throw" => 2,
            "fly" => 3,
            _ => byte.MaxValue,
        };
        return new(
            (byte)bird.state,
            flags,
            checked((ushort)Math.Clamp(bird.segmentIndex, 0, ushort.MaxValue)),
            animation,
            (byte)Math.Clamp(bird.sprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            bird.Position,
            bird.flingSpeed,
            bird.flingTargetSpeed,
            bird.flingAccel,
            bird.sprite?.Position ?? Vector2.Zero,
            bird.sprite?.Scale ?? Vector2.One,
            bird.sprite?.Rotation ?? 0f
        );
    }

    private static WatchEntityState Encode(int id, BirdState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.State;
        payload[1] = state.Flags;
        WatchEntityPayloadCodec.WriteUInt16(payload, 2, state.SegmentIndex);
        payload[4] = state.Animation;
        payload[5] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.FlingSpeed.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.FlingSpeed.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.FlingTargetSpeed.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.FlingTargetSpeed.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.FlingAccel);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.SpritePosition.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 40, state.SpritePosition.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 44, state.SpriteScale.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 48, state.SpriteScale.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 52, state.SpriteRotation);
        return new(new WatchEntityKey(WatchEntityKind.FlingBird, id), payload);
    }

    private static BirdState Decode(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return new(
            payload[0],
            payload[1],
            WatchEntityPayloadCodec.ReadUInt16(payload, 2),
            payload[4],
            payload[5],
            new(
                WatchEntityPayloadCodec.ReadSingle(payload, 8),
                WatchEntityPayloadCodec.ReadSingle(payload, 12)
            ),
            new(
                WatchEntityPayloadCodec.ReadSingle(payload, 16),
                WatchEntityPayloadCodec.ReadSingle(payload, 20)
            ),
            new(
                WatchEntityPayloadCodec.ReadSingle(payload, 24),
                WatchEntityPayloadCodec.ReadSingle(payload, 28)
            ),
            WatchEntityPayloadCodec.ReadSingle(payload, 32),
            new(
                WatchEntityPayloadCodec.ReadSingle(payload, 36),
                WatchEntityPayloadCodec.ReadSingle(payload, 40)
            ),
            new(
                WatchEntityPayloadCodec.ReadSingle(payload, 44),
                WatchEntityPayloadCodec.ReadSingle(payload, 48)
            ),
            WatchEntityPayloadCodec.ReadSingle(payload, 52)
        );
    }

    private static void Apply(FlingBird bird, BirdState state)
    {
        RemoteInfo info = remoteInfos.GetValue(bird, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !info.HasState
            || Vector2.DistanceSquared(bird.Position, state.Position) >= 96f * 96f;
        if (hard)
        {
            bird.Position = state.Position;
            info.PositionStart = info.PositionTarget = state.Position;
            info.SpritePositionStart = info.SpritePositionTarget = state.SpritePosition;
            info.SpriteScaleStart = info.SpriteScaleTarget = state.SpriteScale;
            info.SpriteRotationStart = info.SpriteRotationTarget = state.SpriteRotation;
            info.Elapsed = AnchorInterval;
        }
        else
        {
            info.PositionStart = bird.Position;
            info.PositionTarget = state.Position;
            info.SpritePositionStart = bird.sprite?.Position ?? state.SpritePosition;
            info.SpritePositionTarget = state.SpritePosition;
            info.SpriteScaleStart = bird.sprite?.Scale ?? state.SpriteScale;
            info.SpriteScaleTarget = state.SpriteScale;
            info.SpriteRotationStart = bird.sprite?.Rotation ?? state.SpriteRotation;
            info.SpriteRotationTarget = state.SpriteRotation;
            info.Elapsed = 0f;
        }
        info.HasState = true;
        bird.state = (FlingBird.States)state.State;
        bird.Visible = (state.Flags & VisibleFlag) != 0;
        bird.Collidable = false;
        bird.LightningRemoved = (state.Flags & LightningRemovedFlag) != 0;
        bird.segmentIndex = state.SegmentIndex;
        bird.flingSpeed = state.FlingSpeed;
        bird.flingTargetSpeed = state.FlingTargetSpeed;
        bird.flingAccel = state.FlingAccel;
        if (bird.sprite is null)
            return;
        string? animation = state.Animation switch
        {
            0 => "hover",
            1 => "hoverStressed",
            2 => "throw",
            3 => "fly",
            _ => null,
        };
        if (animation is not null && bird.sprite.Has(animation))
        {
            bool animationChanged = !info.HasAnimation || info.Animation != state.Animation;
            if (animationChanged)
                bird.sprite.Play(animation, restart: true);
            if (hard && bird.sprite.CurrentAnimationTotalFrames > 0)
                bird.sprite.SetAnimationFrame(Math.Min(
                    state.AnimationFrame,
                    bird.sprite.CurrentAnimationTotalFrames - 1
                ));
            info.HasAnimation = true;
            info.Animation = state.Animation;
        }
        if (hard)
        {
            bird.sprite.Position = state.SpritePosition;
            bird.sprite.Scale = state.SpriteScale;
            bird.sprite.Rotation = state.SpriteRotation;
        }
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.FlingBird || state.Key.SubID != 0
            || payload.Length != PayloadSize || payload[0] > 4
            || (payload[1] & ~0b0000_0111) != 0
            || (payload[4] > 3 && payload[4] != byte.MaxValue)
            || payload[6] != 0 || payload[7] != 0)
            return false;
        foreach (int offset in new[] { 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52 })
        {
            if (!float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, offset)))
                return false;
        }
        return WatchEntityPayloadCodec.ReadSingle(payload, 32) is >= 0f and <= 10000f;
    }

    private static void FlingBird_ctor(
        On.Celeste.FlingBird.orig_ctor_EntityData_Vector2 orig,
        FlingBird self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new Info(data.Level.Name, data.ID));
    }

    private static void FlingBird_Update(On.Celeste.FlingBird.orig_Update orig, FlingBird self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        foreach (Coroutine coroutine in self.Components.GetAll<Coroutine>())
            coroutine.Active = false;
        self.Components.Update();
        self.Collidable = false;
        if (!remoteInfos.TryGetValue(self, out RemoteInfo? info) || !info.HasState)
            return;

        info.Elapsed = Math.Min(AnchorInterval, info.Elapsed + Engine.DeltaTime);
        float progress = info.Elapsed / AnchorInterval;
        Vector2 expected = Vector2.Lerp(info.PositionStart, info.PositionTarget, progress);
        if (self.state == FlingBird.States.Fling)
        {
            if (self.flingAccel > 0f)
            {
                self.flingSpeed = Calc.Approach(
                    self.flingSpeed,
                    self.flingTargetSpeed,
                    self.flingAccel * Engine.DeltaTime
                );
                self.Position += self.flingSpeed * Engine.DeltaTime;
            }
            float correction = 1f - MathF.Pow(0.001f, Engine.DeltaTime);
            self.Position = Vector2.Lerp(self.Position, expected, correction);
        }
        else
        {
            self.Position = expected;
        }

        if (self.sprite is not null)
        {
            self.sprite.Position = Vector2.Lerp(
                info.SpritePositionStart,
                info.SpritePositionTarget,
                progress
            );
            self.sprite.Scale = Vector2.Lerp(
                info.SpriteScaleStart,
                info.SpriteScaleTarget,
                progress
            );
            self.sprite.Rotation = MathHelper.Lerp(
                info.SpriteRotationStart,
                info.SpriteRotationTarget,
                progress
            );
        }
        if (self.Visible
            && self.state is (FlingBird.States.Fling
                or FlingBird.States.Move or FlingBird.States.Leaving)
            && Vector2.DistanceSquared(info.LastTrail, self.Position) > 32f * 32f)
        {
            TrailManager.Add(self, self.trailColor, 1f, frozenUpdate: false, useRawDeltaTime: false);
            info.LastTrail = self.Position;
        }
    }

    private static void FlingBird_OnPlayer(
        On.Celeste.FlingBird.orig_OnPlayer orig,
        FlingBird self,
        Player player
    )
    {
        FlingBird.States previous = self.state;
        orig(self, player);
        if (previous != FlingBird.States.Wait
            || WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level || !infos.TryGetValue(self, out Info? info))
            return;
        WatchEntitySyncRegistry.PublishEvent(level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.FlingBird, info.ID), ActivateEvent, []));
    }
}

internal sealed class WatchWallBoosterAdapter : IWatchEntityAdapter
{
    private static readonly WatchWallBoosterAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.WallBooster;

    public static void Load()
    {
        On.Celeste.WallBooster.ctor_EntityData_Vector2 += WallBooster_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.WallBooster.ctor_EntityData_Vector2 -= WallBooster_ctor;
        WatchEntityIDTable<WallBooster>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (WallBooster booster in level.Entities.OfType<WallBooster>())
        {
            if (WatchEntityIDTable<WallBooster>.TryGet(booster, level.Session.Level, out int id))
                yield return new(new WatchEntityKey(Kind, id),
                    [booster.IceMode ? (byte)1 : (byte)0, booster.Visible ? (byte)1 : (byte)0]);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, (bool Ice, bool Visible)> desired = new();
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.Kind != Kind || state.Key.SubID != 0 || payload.Length != 2
                || payload[0] > 1 || payload[1] > 1
                || !desired.TryAdd(state.Key.EntityID, (payload[0] != 0, payload[1] != 0)))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (WallBooster booster in level.Entities.OfType<WallBooster>())
        {
            if (!WatchEntityIDTable<WallBooster>.TryGet(booster, level.Session.Level, out int id)
                || !desired.TryGetValue(id, out var state))
                continue;
            if (booster.IceMode != state.Ice)
                booster.OnChangeMode(state.Ice ? Session.CoreModes.Cold : Session.CoreModes.Hot);
            booster.Visible = state.Visible;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }

    private static void WallBooster_ctor(
        On.Celeste.WallBooster.orig_ctor_EntityData_Vector2 orig,
        WallBooster self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<WallBooster>.Set(self, data.Level.Name, data.ID);
    }
}
