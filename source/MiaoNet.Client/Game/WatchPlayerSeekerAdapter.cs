using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Keeps PlayerSeeker's original renderer while replacing its input-driven
/// Update with watcher-side extrapolation between authoritative anchors. The
/// watcher never reads its own input, collides with its hidden Player, changes
/// the camera, or executes the vanilla chapter-ending callback.
/// </summary>
internal sealed class WatchPlayerSeekerAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 72;
    private const float AnchorInterval = 0.1f;
    private const float HardReanchorDistance = 96f;
    private const float CorrectionResponse = 18f;
    private const float ImmediateSpeedChange = 80f;

    private const byte BreakoutEvent = 1;
    private const byte DashEvent = 2;
    private const byte CollisionEvent = 3;

    private const byte VisibleFlag = 1 << 0;
    private const byte EnabledFlag = 1 << 1;
    private const byte FacingLeftFlag = 1 << 2;
    private const byte CollidableFlag = 1 << 3;
    private const byte ValidFlags = VisibleFlag | EnabledFlag | FacingLeftFlag
        | CollidableFlag;

    private const byte NormalCollision = 0;
    private const byte BarrierCollision = 1;
    private const byte CrackedBlockCollision = 2;

    private static readonly string[] animations =
    [
        "idle",
        "search",
        "spot",
        "spotted",
        "windUp",
        "attacking",
        "takeHit",
        "stunned",
        "pulse",
        "recover",
        "skid",
        "dazed",
        "hatch",
        "statue",
        "flipMouth",
        "flipEyes",
    ];

    private readonly record struct PlayerSeekerState(
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 Speed,
        float DashTimer,
        Vector2 DashDirection,
        float TrailTimerA,
        float TrailTimerB,
        Vector2 Scale,
        float TimeRate,
        float Glitch,
        float Anxiety,
        Vector2 AnxietyOrigin,
        int Depth
    );

    private readonly record struct SyncSignature(
        byte Flags,
        byte Animation,
        Vector2 DashDirection
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private SyncSignature signature;
        private Vector2 speed;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(
            int id,
            PlayerSeekerState current,
            bool forceCurrent,
            float sceneTime
        )
        {
            SyncSignature currentSignature = new(
                current.Flags,
                current.Animation,
                current.DashDirection
            );
            bool speedChanged = hasState
                && Vector2.DistanceSquared(speed, current.Speed)
                    >= ImmediateSpeedChange * ImmediateSpeedChange;
            bool anchorDue = sceneTime >= nextAnchorTime;
            if (forceCurrent || !hasState || currentSignature != signature
                || speedChanged || anchorDue)
            {
                state = Encode(id, current);
                signature = currentSignature;
                speed = current.Speed;
                hasState = true;
                nextAnchorTime = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public PlayerSeekerState State { get; set; }
        public Vector2 PositionError { get; set; }
        public Vector2 SpeedError { get; set; }
        public Vector2 ScaleError { get; set; }

        public void ResetErrors()
        {
            PositionError = Vector2.Zero;
            SpeedError = Vector2.Zero;
            ScaleError = Vector2.Zero;
        }
    }

    private static readonly WatchPlayerSeekerAdapter instance = new();
    private static readonly ConditionalWeakTable<PlayerSeeker, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<PlayerSeeker, RemoteInfo> remoteInfo = new();

    private static bool ownsRemotePresentation;
    private static float previousGlitch;
    private static float previousAnxiety;
    private static Vector2 previousAnxietyOrigin;
    private static Level? remotePresentationLevel;
    private static string? previousColorGrade;
    private static float previousScreenPadding;
    private static bool previousCanRetry;

    public WatchEntityKind Kind => WatchEntityKind.PlayerSeeker;

    public static void Load()
    {
        On.Celeste.PlayerSeeker.ctor += PlayerSeeker_ctor;
        On.Celeste.PlayerSeeker.Awake += PlayerSeeker_Awake;
        On.Celeste.PlayerSeeker.Update += PlayerSeeker_Update;
        On.Celeste.PlayerSeeker.BreakOutParticles += PlayerSeeker_BreakOutParticles;
        On.Celeste.PlayerSeeker.OnPlayer += PlayerSeeker_OnPlayer;
        On.Celeste.PlayerSeeker.OnCollide += PlayerSeeker_OnCollide;
        On.Celeste.PlayerSeeker.Dash += PlayerSeeker_Dash;
        On.Celeste.PlayerSeeker.End += PlayerSeeker_End;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.PlayerSeeker.End -= PlayerSeeker_End;
        On.Celeste.PlayerSeeker.Dash -= PlayerSeeker_Dash;
        On.Celeste.PlayerSeeker.OnCollide -= PlayerSeeker_OnCollide;
        On.Celeste.PlayerSeeker.OnPlayer -= PlayerSeeker_OnPlayer;
        On.Celeste.PlayerSeeker.BreakOutParticles -= PlayerSeeker_BreakOutParticles;
        On.Celeste.PlayerSeeker.Update -= PlayerSeeker_Update;
        On.Celeste.PlayerSeeker.Awake -= PlayerSeeker_Awake;
        On.Celeste.PlayerSeeker.ctor -= PlayerSeeker_ctor;
        WatchEntityIDTable<PlayerSeeker>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        ResetRemoteState();
    }

    public static void ResetRemoteState()
    {
        if (ownsRemotePresentation)
        {
            Glitch.Value = previousGlitch;
            Distort.Anxiety = previousAnxiety;
            Distort.AnxietyOrigin = previousAnxietyOrigin;
            if (remotePresentationLevel is { } level)
            {
                level.Session.ColorGrade = previousColorGrade;
                level.ScreenPadding = previousScreenPadding;
                level.CanRetry = previousCanRetry;
                level.SnapColorGrade(previousColorGrade);
            }
        }
        ownsRemotePresentation = false;
        remotePresentationLevel = null;
        previousColorGrade = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> captured = new();
        foreach (PlayerSeeker seeker in level.Entities.OfType<PlayerSeeker>())
        {
            if (!WatchEntityIDTable<PlayerSeeker>.TryGet(seeker, room, out int id)
                || !captured.Add(id))
                continue;
            PlayerSeekerState current = Capture(seeker);
            yield return syncInfo.GetValue(seeker, static _ => new()).Capture(
                id,
                current,
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
        Dictionary<int, PlayerSeekerState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out PlayerSeekerState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        Dictionary<int, PlayerSeeker> existing = level.Entities.OfType<PlayerSeeker>()
            .Select(entity => (
                Entity: entity,
                HasID: WatchEntityIDTable<PlayerSeeker>.TryGet(entity, room, out int id),
                ID: id
            ))
            .Where(item => item.HasID)
            .GroupBy(item => item.ID)
            .ToDictionary(group => group.Key, group => group.First().Entity);

        foreach ((int id, PlayerSeekerState state) in desired)
        {
            if (!existing.Remove(id, out PlayerSeeker? seeker))
            {
                seeker = Recreate(level, id);
                if (seeker is null)
                    continue;
                changed = true;
            }
            changed |= ApplyAnchor(seeker, state);
        }

        if (isCompleteState)
        {
            foreach (PlayerSeeker seeker in existing.Values)
            {
                DisableLocalBehavior(seeker);
                seeker.Visible = false;
                seeker.RemoveSelf();
                changed = true;
            }
            if (desired.Count == 0)
                ResetRemoteState();
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        PlayerSeeker? seeker = Find(level, entityEvent.Key.EntityID);
        if (seeker is null)
            return;
        switch (entityEvent.EventID)
        {
            case BreakoutEvent:
                seeker.BreakOutParticles();
                break;
            case DashEvent when entityEvent.Payload.Length == 8:
                PlayRemoteDash(
                    level,
                    seeker,
                    new Vector2(
                        WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0),
                        WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 4)
                    )
                );
                break;
            case CollisionEvent when entityEvent.Payload.Length == 2:
                PlayRemoteCollision(
                    level,
                    seeker,
                    entityEvent.Payload.Span[0],
                    entityEvent.Payload.Span[1]
                );
                break;
        }
    }

    private static PlayerSeekerState Capture(PlayerSeeker seeker)
    {
        byte flags = 0;
        if (seeker.Visible) flags |= VisibleFlag;
        if (seeker.enabled) flags |= EnabledFlag;
        if (seeker.facing == Facings.Left) flags |= FacingLeftFlag;
        if (seeker.Collidable) flags |= CollidableFlag;
        return new(
            flags,
            GetAnimation(seeker.sprite.CurrentAnimationID),
            (byte)Math.Clamp(seeker.sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            seeker.Position,
            seeker.speed,
            seeker.dashTimer,
            seeker.dashDirection,
            seeker.trailTimerA,
            seeker.trailTimerB,
            seeker.sprite.Scale,
            Engine.RawDeltaTime > 0f
                ? MathHelper.Clamp(Engine.DeltaTime / Engine.RawDeltaTime, 0f, 2f)
                : 0f,
            Glitch.Value,
            Distort.Anxiety,
            Distort.AnxietyOrigin,
            seeker.Depth
        );
    }

    private static WatchEntityState Encode(int id, PlayerSeekerState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        payload[1] = state.Animation;
        payload[2] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteVector2(payload, 4, state.Position);
        WatchEntityPayloadCodec.WriteVector2(payload, 12, state.Speed);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.DashTimer);
        WatchEntityPayloadCodec.WriteVector2(payload, 24, state.DashDirection);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.TrailTimerA);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.TrailTimerB);
        WatchEntityPayloadCodec.WriteVector2(payload, 40, state.Scale);
        WatchEntityPayloadCodec.WriteSingle(payload, 48, state.TimeRate);
        WatchEntityPayloadCodec.WriteSingle(payload, 52, state.Glitch);
        WatchEntityPayloadCodec.WriteSingle(payload, 56, state.Anxiety);
        WatchEntityPayloadCodec.WriteVector2(payload, 60, state.AnxietyOrigin);
        WatchEntityPayloadCodec.WriteInt32(payload, 68, state.Depth);
        return new(new WatchEntityKey(WatchEntityKind.PlayerSeeker, id), payload);
    }

    private static bool TryDecode(WatchEntityState state, out PlayerSeekerState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.PlayerSeeker
            || state.Key.SubID != 0
            || payload.Length != PayloadSize
            || (payload[0] & ~ValidFlags) != 0
            || payload[1] >= animations.Length
            || payload[3] != 0)
            return false;

        Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
        Vector2 speed = WatchEntityPayloadCodec.ReadVector2(payload, 12);
        float dashTimer = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        Vector2 dashDirection = WatchEntityPayloadCodec.ReadVector2(payload, 24);
        float trailTimerA = WatchEntityPayloadCodec.ReadSingle(payload, 32);
        float trailTimerB = WatchEntityPayloadCodec.ReadSingle(payload, 36);
        Vector2 scale = WatchEntityPayloadCodec.ReadVector2(payload, 40);
        float timeRate = WatchEntityPayloadCodec.ReadSingle(payload, 48);
        float glitch = WatchEntityPayloadCodec.ReadSingle(payload, 52);
        float anxiety = WatchEntityPayloadCodec.ReadSingle(payload, 56);
        Vector2 anxietyOrigin = WatchEntityPayloadCodec.ReadVector2(payload, 60);
        if (!IsFinite(position) || !IsFinite(speed) || !float.IsFinite(dashTimer)
            || !IsFinite(dashDirection) || !float.IsFinite(trailTimerA)
            || !float.IsFinite(trailTimerB) || !IsFinite(scale)
            || timeRate is < 0f or > 2f || glitch is < 0f or > 1f
            || anxiety is < 0f or > 1f || !IsFinite(anxietyOrigin))
            return false;

        value = new(
            payload[0],
            payload[1],
            payload[2],
            position,
            speed,
            dashTimer,
            dashDirection,
            trailTimerA,
            trailTimerB,
            scale,
            timeRate,
            glitch,
            anxiety,
            anxietyOrigin,
            WatchEntityPayloadCodec.ReadInt32(payload, 68)
        );
        return true;
    }

    private static bool ApplyAnchor(PlayerSeeker seeker, PlayerSeekerState state)
    {
        DisableLocalBehavior(seeker);
        RemoteInfo applied = remoteInfo.GetValue(seeker, static _ => new());
        bool lifecycleChanged = applied.HasState
            && ((applied.State.Flags ^ state.Flags) & (VisibleFlag | EnabledFlag)) != 0;
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || lifecycleChanged
            || Vector2.DistanceSquared(seeker.Position, state.Position)
                > HardReanchorDistance * HardReanchorDistance;
        bool changed = !applied.HasState || applied.State != state;

        ApplyDiscrete(seeker, state, hard || !applied.HasState
            || applied.State.Animation != state.Animation);
        if (hard)
        {
            seeker.Position = state.Position;
            seeker.speed = state.Speed;
            seeker.sprite.Scale = state.Scale;
            applied.ResetErrors();
        }
        else
        {
            applied.PositionError = state.Position - seeker.Position;
            applied.SpeedError = state.Speed - seeker.speed;
            applied.ScaleError = state.Scale - seeker.sprite.Scale;
        }
        seeker.dashTimer = state.DashTimer;
        seeker.dashDirection = state.DashDirection;
        seeker.trailTimerA = state.TrailTimerA;
        seeker.trailTimerB = state.TrailTimerB;
        applied.State = state;
        applied.HasState = true;
        ApplyRemotePresentation(seeker, state);
        return changed || hard;
    }

    private static void ApplyDiscrete(
        PlayerSeeker seeker,
        PlayerSeekerState state,
        bool alignFrame
    )
    {
        seeker.Visible = (state.Flags & VisibleFlag) != 0;
        seeker.Collidable = false;
        seeker.enabled = (state.Flags & EnabledFlag) != 0;
        seeker.facing = (state.Flags & FacingLeftFlag) != 0
            ? Facings.Left
            : Facings.Right;
        seeker.Depth = state.Depth;
        string animation = animations[state.Animation];
        if (seeker.sprite.CurrentAnimationID != animation)
            seeker.sprite.Play(animation, restart: true);
        if (alignFrame && seeker.sprite.CurrentAnimationTotalFrames > 0)
        {
            seeker.sprite.SetAnimationFrame(Math.Min(
                state.AnimationFrame,
                seeker.sprite.CurrentAnimationTotalFrames - 1
            ));
        }
    }

    private static void CaptureRemotePresentationBaseline(Level level)
    {
        if (ownsRemotePresentation)
            return;

        previousGlitch = Glitch.Value;
        previousAnxiety = Distort.Anxiety;
        previousAnxietyOrigin = Distort.AnxietyOrigin;
        remotePresentationLevel = level;
        previousColorGrade = level.Session.ColorGrade;
        previousScreenPadding = level.ScreenPadding;
        previousCanRetry = level.CanRetry;
        ownsRemotePresentation = true;
    }

    private static void ApplyRemotePresentation(PlayerSeeker seeker, PlayerSeekerState state)
    {
        if (!ownsRemotePresentation && seeker.Scene is Level level)
            CaptureRemotePresentationBaseline(level);
        Glitch.Value = state.Glitch;
        Distort.Anxiety = state.Anxiety;
        Distort.AnxietyOrigin = state.AnxietyOrigin;
    }

    private static void DisableLocalBehavior(PlayerSeeker seeker)
    {
        seeker.Collidable = false;
        foreach (PlayerCollider collider in seeker.Components.GetAll<PlayerCollider>())
            collider.Active = false;
        foreach (Coroutine coroutine in seeker.Components.GetAll<Coroutine>())
            coroutine.Active = false;
    }

    private static void PlayerSeeker_Update(
        On.Celeste.PlayerSeeker.orig_Update orig,
        PlayerSeeker self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        DisableLocalBehavior(self);
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
        {
            self.Visible = false;
            return;
        }

        PlayerSeekerState state = applied.State;
        ApplyRemotePresentation(self, state);

        // Advance only the original visual components at the watched client's
        // time rate. The global TimeRate is restored before any other entity
        // updates, so PlayerSeeker cannot take ownership of the watcher clock.
        float localDeltaTime = Engine.DeltaTime;
        try
        {
            Engine.DeltaTime = Engine.RawDeltaTime * state.TimeRate;
            self.Components.Update();
        }
        finally
        {
            Engine.DeltaTime = localDeltaTime;
        }

        float deltaTime = Engine.RawDeltaTime * state.TimeRate;
        if (self.enabled && self.sprite.CurrentAnimationID != "hatch")
        {
            if (self.dashTimer > 0f)
            {
                self.speed = Calc.Approach(self.speed, Vector2.Zero, 800f * deltaTime);
                self.dashTimer -= deltaTime;
                if (self.dashTimer <= 0f)
                    self.sprite.Play("spotted");
                UpdateTrailTimer(self, ref self.trailTimerA, deltaTime);
                UpdateTrailTimer(self, ref self.trailTimerB, deltaTime);
                if (self.Scene?.OnInterval(0.04f) == true)
                {
                    Vector2 direction = Calc.SafeNormalize(self.speed);
                    self.SceneAs<Level>().Particles.Emit(
                        Seeker.P_Attack,
                        2,
                        self.Position + direction * 4f,
                        Vector2.One * 4f,
                        Calc.Angle(direction)
                    );
                }
            }
            self.Position += self.speed * deltaTime;
        }

        float correction = deltaTime > 0f
            ? 1f - MathF.Exp(-CorrectionResponse * deltaTime)
            : 0f;
        self.Position += applied.PositionError * correction;
        applied.PositionError *= 1f - correction;
        self.speed += applied.SpeedError * correction;
        applied.SpeedError *= 1f - correction;
        self.sprite.Scale += applied.ScaleError * correction;
        applied.ScaleError *= 1f - correction;
        self.sprite.Scale.X = Calc.Approach(self.sprite.Scale.X, 1f, 2f * deltaTime);
        self.sprite.Scale.Y = Calc.Approach(self.sprite.Scale.Y, 1f, 2f * deltaTime);
    }

    private static void UpdateTrailTimer(
        PlayerSeeker seeker,
        ref float timer,
        float deltaTime
    )
    {
        if (timer <= 0f)
            return;
        timer -= deltaTime;
        if (timer <= 0f)
            seeker.CreateTrail();
    }

    private static void PlayerSeeker_ctor(
        On.Celeste.PlayerSeeker.orig_ctor orig,
        PlayerSeeker self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<PlayerSeeker>.Set(self, data.Level.Name, data.ID);
        if (MiaoNetModule.IsWatching)
            DisableLocalBehavior(self);
    }

    private static void PlayerSeeker_Awake(
        On.Celeste.PlayerSeeker.orig_Awake orig,
        PlayerSeeker self,
        Scene scene
    )
    {
        if (MiaoNetModule.IsWatching && scene is Level level)
            CaptureRemotePresentationBaseline(level);
        orig(self, scene);
        if (MiaoNetModule.IsWatching)
            DisableLocalBehavior(self);
    }

    private static void PlayerSeeker_BreakOutParticles(
        On.Celeste.PlayerSeeker.orig_BreakOutParticles orig,
        PlayerSeeker self
    )
    {
        orig(self);
        if (!MiaoNetModule.IsWatching)
            Publish(self, BreakoutEvent, []);
    }

    private static void PlayerSeeker_Dash(
        On.Celeste.PlayerSeeker.orig_Dash orig,
        PlayerSeeker self,
        Vector2 direction
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self, direction);
        byte[] payload = new byte[8];
        WatchEntityPayloadCodec.WriteVector2(payload, 0, self.dashDirection);
        Publish(self, DashEvent, payload);
    }

    private static void PlayerSeeker_OnCollide(
        On.Celeste.PlayerSeeker.orig_OnCollide orig,
        PlayerSeeker self,
        CollisionData data
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        byte direction = EncodeDirection(data.Direction);
        byte type = data.Hit switch
        {
            SeekerBarrier => BarrierCollision,
            TempleCrackedBlock => CrackedBlockCollision,
            _ => NormalCollision,
        };
        orig(self, data);
        Publish(self, CollisionEvent, [direction, type]);
    }

    private static void PlayerSeeker_OnPlayer(
        On.Celeste.PlayerSeeker.orig_OnPlayer orig,
        PlayerSeeker self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void PlayerSeeker_End(
        On.Celeste.PlayerSeeker.orig_End orig,
        PlayerSeeker self
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self);
    }

    private static void Publish(
        PlayerSeeker seeker,
        byte eventID,
        ReadOnlySpan<byte> payload
    )
    {
        if (WatchEntitySyncRegistry.IsApplyingRemoteState
            || seeker.Scene is not Level level
            || !WatchEntityIDTable<PlayerSeeker>.TryGet(
                seeker,
                level.Session.Level,
                out int id
            ))
            return;
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(
                new WatchEntityKey(WatchEntityKind.PlayerSeeker, id),
                eventID,
                payload
            )
        );
    }

    private static void PlayRemoteDash(
        Level level,
        PlayerSeeker seeker,
        Vector2 direction
    )
    {
        direction = Calc.SafeNormalize(direction);
        if (direction == Vector2.Zero)
            direction = seeker.facing == Facings.Left ? -Vector2.UnitX : Vector2.UnitX;
        seeker.CreateTrail();
        level.DirectionalShake(direction, 0.3f);
        Audio.Play("event:/game/05_mirror_temple/seeker_dash", seeker.Position);
        seeker.sprite.Scale = direction.X == 0f
            ? new Vector2(0.6f, 1.4f)
            : new Vector2(1.4f, 0.6f);
    }

    private static void PlayRemoteCollision(
        Level level,
        PlayerSeeker seeker,
        byte direction,
        byte collisionType
    )
    {
        Vector2 normal = DecodeDirection(direction);
        float angle;
        Vector2 at;
        Vector2 spread;
        if (normal.X > 0f)
        {
            angle = MathF.PI;
            at = new Vector2(seeker.Right, seeker.Y);
            spread = Vector2.UnitY * 4f;
        }
        else if (normal.X < 0f)
        {
            angle = 0f;
            at = new Vector2(seeker.Left, seeker.Y);
            spread = Vector2.UnitY * 4f;
        }
        else if (normal.Y > 0f)
        {
            angle = -MathF.PI / 2f;
            at = new Vector2(seeker.X, seeker.Bottom);
            spread = Vector2.UnitX * 4f;
        }
        else
        {
            angle = MathF.PI / 2f;
            at = new Vector2(seeker.X, seeker.Top);
            spread = Vector2.UnitX * 4f;
        }
        level.Particles.Emit(Seeker.P_HitWall, 12, at, spread, angle);
        Audio.Play(
            collisionType == BarrierCollision
                ? "event:/game/05_mirror_temple/seeker_hit_lightwall"
                : "event:/game/05_mirror_temple/seeker_hit_normal",
            seeker.Position
        );
    }

    private static PlayerSeeker? Recreate(Level level, int id)
    {
        LevelData levelData = level.Session.MapData.Get(level.Session.Level);
        EntityData? data = levelData.Entities.FirstOrDefault(candidate =>
            candidate.ID == id && candidate.Name == "playerSeeker"
        );
        if (data is null)
            return null;
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        PlayerSeeker seeker = new(data, offset);
        WatchEntityIDTable<PlayerSeeker>.Set(seeker, level.Session.Level, id);
        level.Add(seeker);
        return seeker;
    }

    private static PlayerSeeker? Find(Level level, int id)
        => WatchEntityIDTable<PlayerSeeker>.Find(level, id);

    private static byte GetAnimation(string? animation)
    {
        int index = Array.IndexOf(animations, animation);
        return (byte)(index >= 0 ? index : 0);
    }

    private static byte EncodeDirection(Vector2 direction)
        => Math.Abs(direction.X) >= Math.Abs(direction.Y)
            ? direction.X >= 0f ? (byte)0 : (byte)1
            : direction.Y >= 0f ? (byte)2 : (byte)3;

    private static Vector2 DecodeDirection(byte direction)
        => direction switch
        {
            0 => Vector2.UnitX,
            1 => -Vector2.UnitX,
            2 => Vector2.UnitY,
            _ => -Vector2.UnitY,
        };

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
