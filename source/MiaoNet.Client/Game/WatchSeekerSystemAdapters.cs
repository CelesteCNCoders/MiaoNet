using MiaoNet.Shared;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchSeekerSystemAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 44;
    private const float AnchorInterval = 0.1f;
    private const float HardReanchorDistance = 96f;
    private const float CorrectionResponse = 18f;
    private const float ImmediateSpeedChange = 80f;
    private const float BouncedFreezeDuration = 0.15f;
    private const float RecoveryDeceleration = 150f;

    private const byte StatueBreakoutEvent = 1;
    private const byte AttackEvent = 2;
    private const byte WallHitEvent = 3;
    private const byte BouncedEvent = 4;
    private const byte RegenerateBeginEvent = 5;
    private const byte RegenerateEndEvent = 6;

    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte DeadFlag = 1 << 2;
    private const byte SpottedFlag = 1 << 3;
    private const byte CanSeePlayerFlag = 1 << 4;
    private const byte AttackWindUpFlag = 1 << 5;
    private const byte StrongSkidFlag = 1 << 6;
    private const byte ValidFlags = VisibleFlag | CollidableFlag | DeadFlag | SpottedFlag
        | CanSeePlayerFlag | AttackWindUpFlag | StrongSkidFlag;

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

    private readonly record struct SeekerState(
        WatchSeekerForm Form,
        WatchSeekerPhase Phase,
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        int Facing,
        int SpriteFacing,
        Vector2 Position,
        Vector2 Speed,
        Vector2 Scale,
        float LightAlpha,
        float AttackSpeed,
        int Depth
    );

    private readonly record struct SyncSignature(
        WatchSeekerForm Form,
        WatchSeekerPhase Phase,
        byte Flags,
        byte Animation,
        int Facing,
        int SpriteFacing
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
            SeekerState current,
            bool forceCurrent,
            float sceneTime
        )
        {
            SyncSignature currentSignature = new(
                current.Form,
                current.Phase,
                current.Flags,
                current.Animation,
                current.Facing,
                current.SpriteFacing
            );
            bool speedChanged = hasState
                && Vector2.DistanceSquared(speed, current.Speed)
                    >= ImmediateSpeedChange * ImmediateSpeedChange;
            bool movingAnchor = current.Form == WatchSeekerForm.Seeker
                && current.Speed.LengthSquared() > 1f
                && sceneTime >= nextAnchorTime;
            if (forceCurrent || !hasState || currentSignature != signature
                || speedChanged || movingAnchor)
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
        public SeekerState State { get; set; }
        public Vector2 PositionError { get; set; }
        public Vector2 SpeedError { get; set; }
        public Vector2 ScaleError { get; set; }
        public float LightAlphaError { get; set; }
        public float RegenerateParticleTimer { get; set; }

        public void ResetErrors()
        {
            PositionError = Vector2.Zero;
            SpeedError = Vector2.Zero;
            ScaleError = Vector2.Zero;
            LightAlphaError = 0f;
        }
    }

    private static readonly WatchSeekerSystemAdapter instance = new();
    private static readonly ConditionalWeakTable<Entity, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<Seeker, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.SeekerSystem;

    public static void Load()
    {
        On.Celeste.Seeker.ctor_EntityData_Vector2 += Seeker_ctor;
        On.Celeste.Seeker.Added += Seeker_Added;
        On.Celeste.Seeker.Awake += Seeker_Awake;
        On.Celeste.Seeker.Update += Seeker_Update;
        On.Celeste.Seeker.OnAttackPlayer += Seeker_OnAttackPlayer;
        On.Celeste.Seeker.OnBouncePlayer += Seeker_OnBouncePlayer;
        On.Celeste.Seeker.GotBouncedOn += Seeker_GotBouncedOn;
        On.Celeste.Seeker.HitSpring += Seeker_HitSpring;
        On.Celeste.Seeker.OnHoldable += Seeker_OnHoldable;
        On.Celeste.Seeker.SlammedIntoWall += Seeker_SlammedIntoWall;
        On.Celeste.Seeker.AttackBegin += Seeker_AttackBegin;
        On.Celeste.Seeker.RegenerateBegin += Seeker_RegenerateBegin;
        On.Celeste.Seeker.RegenerateEnd += Seeker_RegenerateEnd;
        On.Celeste.SeekerStatue.ctor += SeekerStatue_ctor;
        On.Celeste.SeekerStatue.Update += SeekerStatue_Update;
        On.Celeste.SeekerStatue.BreakOutParticles += SeekerStatue_BreakOutParticles;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.SeekerStatue.BreakOutParticles -= SeekerStatue_BreakOutParticles;
        On.Celeste.SeekerStatue.Update -= SeekerStatue_Update;
        On.Celeste.SeekerStatue.ctor -= SeekerStatue_ctor;
        On.Celeste.Seeker.RegenerateEnd -= Seeker_RegenerateEnd;
        On.Celeste.Seeker.RegenerateBegin -= Seeker_RegenerateBegin;
        On.Celeste.Seeker.AttackBegin -= Seeker_AttackBegin;
        On.Celeste.Seeker.SlammedIntoWall -= Seeker_SlammedIntoWall;
        On.Celeste.Seeker.OnHoldable -= Seeker_OnHoldable;
        On.Celeste.Seeker.HitSpring -= Seeker_HitSpring;
        On.Celeste.Seeker.GotBouncedOn -= Seeker_GotBouncedOn;
        On.Celeste.Seeker.OnBouncePlayer -= Seeker_OnBouncePlayer;
        On.Celeste.Seeker.OnAttackPlayer -= Seeker_OnAttackPlayer;
        On.Celeste.Seeker.Update -= Seeker_Update;
        On.Celeste.Seeker.Awake -= Seeker_Awake;
        On.Celeste.Seeker.Added -= Seeker_Added;
        On.Celeste.Seeker.ctor_EntityData_Vector2 -= Seeker_ctor;
        WatchEntityIDTable<Seeker>.Clear();
        WatchEntityIDTable<SeekerStatue>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> captured = new();
        foreach (Seeker seeker in level.Entities.OfType<Seeker>())
        {
            if (!WatchEntityIDTable<Seeker>.TryGet(seeker, room, out int id)
                || !captured.Add(id))
                continue;
            SeekerState current = Capture(seeker);
            yield return syncInfo.GetValue(seeker, static _ => new()).Capture(
                id,
                current,
                WatchEntitySyncRegistry.IsCapturingCurrentState,
                level.TimeActive
            );
        }

        foreach (SeekerStatue statue in level.Entities.OfType<SeekerStatue>())
        {
            if (!WatchEntityIDTable<SeekerStatue>.TryGet(statue, room, out int id)
                || !captured.Add(id))
                continue;
            SeekerState current = Capture(statue);
            yield return syncInfo.GetValue(statue, static _ => new()).Capture(
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
        Dictionary<int, SeekerState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out SeekerState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        Dictionary<int, Seeker> seekers = level.Entities.OfType<Seeker>()
            .Select(entity => (
                Entity: entity,
                HasID: WatchEntityIDTable<Seeker>.TryGet(entity, room, out int id),
                ID: id
            ))
            .Where(item => item.HasID)
            .GroupBy(item => item.ID)
            .ToDictionary(group => group.Key, group => group.First().Entity);
        Dictionary<int, SeekerStatue> statues = level.Entities.OfType<SeekerStatue>()
            .Select(entity => (
                Entity: entity,
                HasID: WatchEntityIDTable<SeekerStatue>.TryGet(entity, room, out int id),
                ID: id
            ))
            .Where(item => item.HasID)
            .GroupBy(item => item.ID)
            .ToDictionary(group => group.Key, group => group.First().Entity);

        foreach ((int id, SeekerState state) in desired)
        {
            if (state.Form == WatchSeekerForm.Seeker)
            {
                if (statues.Remove(id, out SeekerStatue? statue))
                {
                    statue.Visible = false;
                    statue.RemoveSelf();
                    changed = true;
                }
                if (!seekers.Remove(id, out Seeker? seeker))
                {
                    seeker = new Seeker(state.Position, []);
                    WatchEntityIDTable<Seeker>.Set(seeker, room, id);
                    level.Add(seeker);
                    changed = true;
                }
                changed |= ApplySeekerAnchor(seeker, state);
            }
            else
            {
                if (seekers.Remove(id, out Seeker? seeker))
                {
                    DisableLocalBehavior(seeker);
                    seeker.Visible = false;
                    seeker.RemoveSelf();
                    changed = true;
                }
                if (!statues.Remove(id, out SeekerStatue? statue))
                {
                    statue = RecreateStatue(level, id);
                    if (statue is null)
                        continue;
                    changed = true;
                }
                changed |= ApplyStatue(statue, state);
            }
        }

        if (isCompleteState)
        {
            foreach (Seeker seeker in seekers.Values)
            {
                DisableLocalBehavior(seeker);
                seeker.Visible = false;
                seeker.RemoveSelf();
                changed = true;
            }
            foreach (SeekerStatue statue in statues.Values)
            {
                statue.Visible = false;
                statue.RemoveSelf();
                changed = true;
            }
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        int id = entityEvent.Key.EntityID;
        switch (entityEvent.EventID)
        {
            case StatueBreakoutEvent:
                FindStatue(level, id)?.BreakOutParticles();
                break;
            case AttackEvent:
                if (FindSeeker(level, id) is { } attacking)
                {
                    Audio.Play("event:/game/05_mirror_temple/seeker_dash", attacking.Position);
                    level.DirectionalShake(Calc.SafeNormalize(attacking.Speed), 0.15f);
                }
                break;
            case WallHitEvent when entityEvent.Payload.Length == 17:
                if (FindSeeker(level, id) is { } wallHit)
                {
                    ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
                    wallHit.Position = WatchEntityPayloadCodec.ReadVector2(payload, 1);
                    wallHit.Speed = WatchEntityPayloadCodec.ReadVector2(payload, 9);
                    if (remoteInfo.TryGetValue(wallHit, out RemoteInfo? wallInfo))
                    {
                        wallInfo.PositionError = Vector2.Zero;
                        wallInfo.SpeedError = Vector2.Zero;
                    }
                    PlayWallHit(wallHit, payload[0]);
                }
                break;
            case BouncedEvent:
                if (FindSeeker(level, id) is { } bounced)
                    PlayBounced(bounced);
                break;
            case RegenerateBeginEvent:
                if (FindSeeker(level, id) is { } regenerating)
                {
                    regenerating.RegenerateBegin();
                    remoteInfo.GetValue(regenerating, static _ => new()).RegenerateParticleTimer = 0f;
                }
                break;
            case RegenerateEndEvent:
                if (FindSeeker(level, id) is { } regenerated)
                    regenerated.RegenerateEnd();
                break;
        }
    }

    private static SeekerState Capture(Seeker seeker)
    {
        byte flags = 0;
        if (seeker.Visible) flags |= VisibleFlag;
        if (seeker.Collidable) flags |= CollidableFlag;
        if (seeker.dead) flags |= DeadFlag;
        if (seeker.spotted) flags |= SpottedFlag;
        if (seeker.canSeePlayer) flags |= CanSeePlayerFlag;
        if (seeker.attackWindUp) flags |= AttackWindUpFlag;
        if (seeker.strongSkid) flags |= StrongSkidFlag;
        return new(
            WatchSeekerForm.Seeker,
            (WatchSeekerPhase)Math.Clamp(
                seeker.State.State,
                (int)WatchSeekerPhase.Idle,
                (int)WatchSeekerPhase.Returned
            ),
            flags,
            GetAnimation(seeker.sprite.CurrentAnimationID),
            (byte)Math.Clamp(seeker.sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            seeker.facing < 0 ? -1 : 1,
            seeker.spriteFacing < 0 ? -1 : 1,
            seeker.Position,
            seeker.Speed,
            seeker.sprite.Scale,
            seeker.Light.Alpha,
            seeker.attackSpeed,
            seeker.Depth
        );
    }

    private static SeekerState Capture(SeekerStatue statue)
    {
        string animation = statue.sprite.CurrentAnimationID;
        byte flags = 0;
        if (statue.Visible) flags |= VisibleFlag;
        if (statue.Collidable) flags |= CollidableFlag;
        return new(
            animation == "hatch" ? WatchSeekerForm.Hatching : WatchSeekerForm.Statue,
            WatchSeekerPhase.Idle,
            flags,
            GetAnimation(animation),
            (byte)Math.Clamp(statue.sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            1,
            1,
            statue.Position,
            Vector2.Zero,
            statue.sprite.Scale,
            0f,
            0f,
            statue.Depth
        );
    }

    private static WatchEntityState Encode(int id, SeekerState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = (byte)state.Form;
        payload[1] = (byte)state.Phase;
        payload[2] = state.Flags;
        payload[3] = state.Animation;
        payload[4] = state.AnimationFrame;
        payload[5] = state.Facing < 0 ? (byte)0 : (byte)1;
        payload[6] = state.SpriteFacing < 0 ? (byte)0 : (byte)1;
        WatchEntityPayloadCodec.WriteVector2(payload, 8, state.Position);
        WatchEntityPayloadCodec.WriteVector2(payload, 16, state.Speed);
        WatchEntityPayloadCodec.WriteVector2(payload, 24, state.Scale);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.LightAlpha);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.AttackSpeed);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(40), state.Depth);
        return new(new WatchEntityKey(WatchEntityKind.SeekerSystem, id), payload);
    }

    private static bool TryDecode(WatchEntityState state, out SeekerState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.SeekerSystem
            || state.Key.SubID != 0
            || payload.Length != PayloadSize
            || payload[0] > (byte)WatchSeekerForm.Seeker
            || payload[1] > (byte)WatchSeekerPhase.Returned
            || (payload[2] & ~ValidFlags) != 0
            || payload[3] >= animations.Length
            || payload[5] > 1 || payload[6] > 1 || payload[7] != 0
            || (payload[0] != (byte)WatchSeekerForm.Seeker && payload[1] != 0))
            return false;

        Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 8);
        Vector2 speed = WatchEntityPayloadCodec.ReadVector2(payload, 16);
        Vector2 scale = WatchEntityPayloadCodec.ReadVector2(payload, 24);
        float lightAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 32);
        float attackSpeed = WatchEntityPayloadCodec.ReadSingle(payload, 36);
        if (!IsFinite(position) || !IsFinite(speed) || !IsFinite(scale)
            || !float.IsFinite(lightAlpha) || !float.IsFinite(attackSpeed))
            return false;

        value = new(
            (WatchSeekerForm)payload[0],
            (WatchSeekerPhase)payload[1],
            payload[2],
            payload[3],
            payload[4],
            payload[5] == 0 ? -1 : 1,
            payload[6] == 0 ? -1 : 1,
            position,
            speed,
            scale,
            lightAlpha,
            attackSpeed,
            BinaryPrimitives.ReadInt32LittleEndian(payload[40..])
        );
        return true;
    }

    private static bool ApplySeekerAnchor(Seeker seeker, SeekerState state)
    {
        DisableLocalBehavior(seeker);
        RemoteInfo applied = remoteInfo.GetValue(seeker, static _ => new());
        bool phaseChanged = applied.HasState && applied.State.Phase != state.Phase;
        bool lifecycleChanged = applied.HasState
            && ((applied.State.Flags ^ state.Flags) & (VisibleFlag | DeadFlag)) != 0;
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || phaseChanged
            || lifecycleChanged
            || Vector2.DistanceSquared(seeker.Position, state.Position)
                > HardReanchorDistance * HardReanchorDistance;
        bool changed = !applied.HasState || applied.State != state;

        ApplySeekerDiscrete(seeker, state, hard || !applied.HasState
            || applied.State.Animation != state.Animation);
        if (hard)
        {
            seeker.Position = state.Position;
            seeker.Speed = state.Speed;
            seeker.sprite.Scale = state.Scale;
            seeker.Light.Alpha = state.LightAlpha;
            seeker.attackSpeed = state.AttackSpeed;
            applied.ResetErrors();
        }
        else
        {
            applied.PositionError = state.Position - seeker.Position;
            applied.SpeedError = state.Speed - seeker.Speed;
            applied.ScaleError = state.Scale - seeker.sprite.Scale;
            applied.LightAlphaError = state.LightAlpha - seeker.Light.Alpha;
            seeker.attackSpeed = state.AttackSpeed;
        }
        applied.State = state;
        applied.HasState = true;
        return changed || hard;
    }

    private static void ApplySeekerDiscrete(Seeker seeker, SeekerState state, bool alignFrame)
    {
        seeker.Visible = (state.Flags & VisibleFlag) != 0;
        seeker.Collidable = false;
        seeker.dead = (state.Flags & DeadFlag) != 0;
        seeker.spotted = (state.Flags & SpottedFlag) != 0;
        seeker.canSeePlayer = (state.Flags & CanSeePlayerFlag) != 0;
        seeker.attackWindUp = (state.Flags & AttackWindUpFlag) != 0;
        seeker.strongSkid = (state.Flags & StrongSkidFlag) != 0;
        seeker.facing = state.Facing;
        seeker.spriteFacing = state.SpriteFacing;
        seeker.Depth = state.Depth;
        seeker.Light.StartRadius = state.Phase == WatchSeekerPhase.Regenerate ? 16f : 32f;
        seeker.Light.EndRadius = state.Phase == WatchSeekerPhase.Regenerate ? 32f : 64f;
        ApplyAnimation(seeker.sprite, state.Animation, state.AnimationFrame, alignFrame);
    }

    private static bool ApplyStatue(SeekerStatue statue, SeekerState state)
    {
        bool visible = (state.Flags & VisibleFlag) != 0;
        bool changed = statue.Position != state.Position || statue.Visible != visible
            || statue.Depth != state.Depth
            || statue.sprite.CurrentAnimationID != animations[state.Animation];
        statue.Position = state.Position;
        statue.Visible = visible;
        statue.Collidable = false;
        statue.Depth = state.Depth;
        statue.sprite.Scale = state.Scale;
        statue.sprite.OnLastFrame = null;
        ApplyAnimation(statue.sprite, state.Animation, state.AnimationFrame, alignFrame: true);
        return changed;
    }

    private static SeekerStatue? RecreateStatue(Level level, int id)
    {
        LevelData levelData = level.Session.MapData.Get(level.Session.Level);
        EntityData? data = levelData.Entities.FirstOrDefault(candidate =>
            candidate.ID == id && candidate.Name == "seekerStatue"
        );
        if (data is null)
            return null;
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        SeekerStatue statue = new(data, offset);
        WatchEntityIDTable<SeekerStatue>.Set(statue, level.Session.Level, id);
        level.Add(statue);
        return statue;
    }

    private static void ApplyAnimation(Sprite sprite, byte animation, byte frame, bool alignFrame)
    {
        string id = animations[animation];
        if (sprite.CurrentAnimationID != id)
            sprite.Play(id, restart: true);
        if (alignFrame && sprite.CurrentAnimationTotalFrames > 0)
            sprite.SetAnimationFrame(Math.Min(frame, sprite.CurrentAnimationTotalFrames - 1));
    }

    private static byte GetAnimation(string? animation)
    {
        int index = Array.IndexOf(animations, animation);
        return (byte)(index >= 0 ? index : 0);
    }

    private static void DisableLocalBehavior(Seeker seeker)
    {
        seeker.Collidable = false;
        seeker.State.Active = false;
        seeker.State.Locked = true;
        foreach (PlayerCollider collider in seeker.Components.GetAll<PlayerCollider>())
            collider.Active = false;
        foreach (HoldableCollider collider in seeker.Components.GetAll<HoldableCollider>())
            collider.Active = false;
    }

    private static void Seeker_Update(On.Celeste.Seeker.orig_Update orig, Seeker self)
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

        SeekerState state = applied.State;
        self.Components.Update();
        float deltaTime = Engine.DeltaTime;
        if (state.Phase == WatchSeekerPhase.Regenerate)
        {
            // Match RegenerateUpdate: the horizontal component is damped once
            // on its own, then the complete velocity approaches zero.
            self.Speed.X = Calc.Approach(self.Speed.X, 0f, RecoveryDeceleration * deltaTime);
            self.Speed = Calc.Approach(
                self.Speed,
                Vector2.Zero,
                RecoveryDeceleration * deltaTime
            );
            applied.RegenerateParticleTimer -= deltaTime;
            if (applied.RegenerateParticleTimer <= 0f && self.Scene is Level regenLevel)
            {
                applied.RegenerateParticleTimer += 0.04f;
                regenLevel.Particles.Emit(
                    Seeker.P_Regen,
                    self.Center + Calc.AngleToVector(Calc.Random.NextFloat(MathF.Tau), 6f),
                    Calc.Random.NextFloat(MathF.Tau)
                );
            }
        }
        else if (state.Phase == WatchSeekerPhase.Stunned)
        {
            self.Speed = Calc.Approach(
                self.Speed,
                Vector2.Zero,
                RecoveryDeceleration * deltaTime
            );
        }
        self.Position += self.Speed * deltaTime;

        float correction = 1f - MathF.Exp(-CorrectionResponse * deltaTime);
        self.Position += applied.PositionError * correction;
        applied.PositionError *= 1f - correction;
        self.Speed += applied.SpeedError * correction;
        applied.SpeedError *= 1f - correction;
        self.sprite.Scale += applied.ScaleError * correction;
        applied.ScaleError *= 1f - correction;
        self.Light.Alpha += applied.LightAlphaError * correction;
        applied.LightAlphaError *= 1f - correction;
        self.sprite.Scale.X = Calc.Approach(self.sprite.Scale.X, 1f, 2f * deltaTime);
        self.sprite.Scale.Y = Calc.Approach(self.sprite.Scale.Y, 1f, 2f * deltaTime);
        if (state.Phase == WatchSeekerPhase.Attack
            && self.Scene?.OnInterval(0.05f) == true)
            self.CreateTrail();
    }

    private static void SeekerStatue_Update(
        On.Celeste.SeekerStatue.orig_Update orig,
        SeekerStatue self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        self.Collidable = false;
        self.sprite.OnLastFrame = null;
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            self.Components.Update();
    }

    private static void Seeker_ctor(
        On.Celeste.Seeker.orig_ctor_EntityData_Vector2 orig,
        Seeker self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Seeker>.Set(self, data.Level.Name, data.ID);
    }

    private static void SeekerStatue_ctor(
        On.Celeste.SeekerStatue.orig_ctor orig,
        SeekerStatue self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<SeekerStatue>.Set(self, data.Level.Name, data.ID);
    }

    private static void Seeker_Added(
        On.Celeste.Seeker.orig_Added orig,
        Seeker self,
        Scene scene
    )
    {
        orig(self, scene);
        ReapplyAfterLifecycleCallback(self);
    }

    private static void Seeker_Awake(
        On.Celeste.Seeker.orig_Awake orig,
        Seeker self,
        Scene scene
    )
    {
        orig(self, scene);
        ReapplyAfterLifecycleCallback(self);
    }

    private static void ReapplyAfterLifecycleCallback(Seeker seeker)
    {
        if (MiaoNetModule.IsWatching
            && remoteInfo.TryGetValue(seeker, out RemoteInfo? applied)
            && applied.HasState)
        {
            ApplySeekerDiscrete(seeker, applied.State, alignFrame: true);
            seeker.Position = applied.State.Position;
            seeker.Speed = applied.State.Speed;
            applied.ResetErrors();
        }
    }

    private static void Seeker_OnAttackPlayer(
        On.Celeste.Seeker.orig_OnAttackPlayer orig,
        Seeker self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void Seeker_OnBouncePlayer(
        On.Celeste.Seeker.orig_OnBouncePlayer orig,
        Seeker self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void Seeker_GotBouncedOn(
        On.Celeste.Seeker.orig_GotBouncedOn orig,
        Seeker self,
        Entity entity
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self, entity);
        Publish(self, BouncedEvent, []);
    }

    private static void Seeker_HitSpring(On.Celeste.Seeker.orig_HitSpring orig, Seeker self)
    {
        if (!MiaoNetModule.IsWatching)
            orig(self);
    }

    private static void Seeker_OnHoldable(
        On.Celeste.Seeker.orig_OnHoldable orig,
        Seeker self,
        Holdable holdable
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, holdable);
    }

    private static void Seeker_SlammedIntoWall(
        On.Celeste.Seeker.orig_SlammedIntoWall orig,
        Seeker self,
        CollisionData data
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        byte direction = EncodeDirection(data.Direction);
        orig(self, data);
        byte[] payload = new byte[17];
        payload[0] = direction;
        WatchEntityPayloadCodec.WriteVector2(payload, 1, self.Position);
        WatchEntityPayloadCodec.WriteVector2(payload, 9, self.Speed);
        Publish(self, WallHitEvent, payload);
    }

    private static void Seeker_AttackBegin(
        On.Celeste.Seeker.orig_AttackBegin orig,
        Seeker self
    )
    {
        orig(self);
        if (!MiaoNetModule.IsWatching)
            Publish(self, AttackEvent, []);
    }

    private static void Seeker_RegenerateBegin(
        On.Celeste.Seeker.orig_RegenerateBegin orig,
        Seeker self
    )
    {
        orig(self);
        if (!MiaoNetModule.IsWatching)
            Publish(self, RegenerateBeginEvent, []);
    }

    private static void Seeker_RegenerateEnd(
        On.Celeste.Seeker.orig_RegenerateEnd orig,
        Seeker self
    )
    {
        orig(self);
        if (!MiaoNetModule.IsWatching)
            Publish(self, RegenerateEndEvent, []);
    }

    private static void SeekerStatue_BreakOutParticles(
        On.Celeste.SeekerStatue.orig_BreakOutParticles orig,
        SeekerStatue self
    )
    {
        orig(self);
        if (!MiaoNetModule.IsWatching
            && self.Scene is Level level
            && WatchEntityIDTable<SeekerStatue>.TryGet(
                self,
                level.Session.Level,
                out int id
            ))
        {
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(
                    new WatchEntityKey(WatchEntityKind.SeekerSystem, id),
                    StatueBreakoutEvent,
                    []
                )
            );
        }
    }

    private static void Publish(Seeker seeker, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (WatchEntitySyncRegistry.IsApplyingRemoteState
            || seeker.Scene is not Level level
            || !WatchEntityIDTable<Seeker>.TryGet(
                seeker,
                level.Session.Level,
                out int id
            ))
            return;
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(
                new WatchEntityKey(WatchEntityKind.SeekerSystem, id),
                eventID,
                payload
            )
        );
    }

    private static void PlayWallHit(Seeker seeker, byte direction)
    {
        Vector2 normal = DecodeDirection(direction);
        Audio.Play("event:/game/05_mirror_temple/seeker_hit_normal", seeker.Position);
        seeker.shaker.ShakeFor(0.5f, false);
        seeker.scaleWiggler.Start();
        seeker.SceneAs<Level>().DirectionalShake(normal, 0.2f);
        seeker.SceneAs<Level>().Particles.Emit(
            Seeker.P_HitWall,
            12,
            seeker.Center,
            Vector2.One * 4f,
            Calc.Angle(-normal)
        );
    }

    private static void PlayBounced(Seeker seeker)
    {
        // GotBouncedOn freezes the producer before its downward recovery
        // velocity is integrated. Replaying the hit-stop prevents the watcher
        // from advancing roughly 30 pixels during that missing 0.15 second.
        Celeste.Freeze(BouncedFreezeDuration);
        seeker.sprite.Scale = new Vector2(1.4f, 0.6f);
        seeker.SceneAs<Level>().Particles.Emit(
            Seeker.P_Stomp,
            8,
            seeker.Center - Vector2.UnitY * 5f,
            new Vector2(6f, 3f)
        );
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
            3 => -Vector2.UnitY,
            _ => Vector2.Zero,
        };

    private static Seeker? FindSeeker(Level level, int id)
        => WatchEntityIDTable<Seeker>.Find(level, id);

    private static SeekerStatue? FindStatue(Level level, int id)
        => WatchEntityIDTable<SeekerStatue>.Find(level, id);

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);
}

internal sealed class WatchSeekerBarrierAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 16;
    private const byte ReflectEvent = 1;
    private const byte FlashingFlag = 1 << 0;

    private readonly record struct BarrierState(
        bool Flashing,
        float Flash,
        float Solidify,
        float SolidifyDelay
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, BarrierState current, bool forceCurrent)
        {
            if (forceCurrent || !hasState)
            {
                state = Encode(id, current);
                hasState = true;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasNetworkState { get; set; }
        public BarrierState NetworkState { get; set; }
    }

    private static readonly WatchSeekerBarrierAdapter instance = new();
    private static readonly ConditionalWeakTable<SeekerBarrier, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<SeekerBarrier, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.SeekerBarrier;

    public static void Load()
    {
        On.Celeste.SeekerBarrier.ctor_EntityData_Vector2 += SeekerBarrier_ctor;
        On.Celeste.SeekerBarrier.Update += SeekerBarrier_Update;
        On.Celeste.SeekerBarrier.OnReflectSeeker += SeekerBarrier_OnReflectSeeker;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.SeekerBarrier.OnReflectSeeker -= SeekerBarrier_OnReflectSeeker;
        On.Celeste.SeekerBarrier.Update -= SeekerBarrier_Update;
        On.Celeste.SeekerBarrier.ctor_EntityData_Vector2 -= SeekerBarrier_ctor;
        WatchEntityIDTable<SeekerBarrier>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (SeekerBarrier barrier in level.Entities.OfType<SeekerBarrier>())
        {
            if (!WatchEntityIDTable<SeekerBarrier>.TryGet(barrier, room, out int id))
                continue;
            yield return syncInfo.GetValue(barrier, static _ => new()).Capture(
                id,
                Capture(barrier),
                WatchEntitySyncRegistry.IsCapturingCurrentState
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, BarrierState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out BarrierState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (SeekerBarrier barrier in level.Entities.OfType<SeekerBarrier>())
        {
            barrier.Collidable = false;
            if (!WatchEntityIDTable<SeekerBarrier>.TryGet(barrier, room, out int id)
                || !desired.Remove(id, out BarrierState state))
                continue;
            RemoteInfo applied = remoteInfo.GetValue(barrier, static _ => new());
            if (!applied.HasNetworkState
                || WatchEntitySyncRegistry.IsApplyingLifecycleReset
                || applied.NetworkState != state)
            {
                Apply(barrier, state);
                applied.NetworkState = state;
                applied.HasNetworkState = true;
                changed = true;
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        SeekerBarrier? barrier = Find(level, entityEvent.Key.EntityID);
        if (barrier is null || entityEvent.EventID != ReflectEvent)
            return;
        barrier.Flash = 1f;
        barrier.Solidify = 1f;
        barrier.solidifyDelay = 1f;
        barrier.Flashing = true;
    }

    private static BarrierState Capture(SeekerBarrier barrier)
        => new(barrier.Flashing, barrier.Flash, barrier.Solidify, barrier.solidifyDelay);

    private static WatchEntityState Encode(int id, BarrierState state)
    {
        byte[] payload = new byte[PayloadSize];
        if (state.Flashing) payload[0] = FlashingFlag;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Flash);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Solidify);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.SolidifyDelay);
        return new(new WatchEntityKey(WatchEntityKind.SeekerBarrier, id), payload);
    }

    private static bool TryDecode(WatchEntityState state, out BarrierState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.SeekerBarrier
            || state.Key.SubID != 0
            || payload.Length != PayloadSize
            || (payload[0] & ~FlashingFlag) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        float flash = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        float solidify = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        float delay = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        if (!float.IsFinite(flash) || !float.IsFinite(solidify) || !float.IsFinite(delay))
            return false;
        value = new((payload[0] & FlashingFlag) != 0, flash, solidify, delay);
        return true;
    }

    private static void Apply(SeekerBarrier barrier, BarrierState state)
    {
        barrier.Collidable = false;
        barrier.Flashing = state.Flashing;
        barrier.Flash = state.Flash;
        barrier.Solidify = state.Solidify;
        barrier.solidifyDelay = state.SolidifyDelay;
    }

    private static void SeekerBarrier_ctor(
        On.Celeste.SeekerBarrier.orig_ctor_EntityData_Vector2 orig,
        SeekerBarrier self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<SeekerBarrier>.Set(self, data.Level.Name, data.ID);
    }

    private static void SeekerBarrier_Update(
        On.Celeste.SeekerBarrier.orig_Update orig,
        SeekerBarrier self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            orig(self);
        self.Collidable = false;
    }

    private static void SeekerBarrier_OnReflectSeeker(
        On.Celeste.SeekerBarrier.orig_OnReflectSeeker orig,
        SeekerBarrier self
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self);
        if (self.Scene is Level level
            && WatchEntityIDTable<SeekerBarrier>.TryGet(
                self,
                level.Session.Level,
                out int id
            ))
        {
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(
                    new WatchEntityKey(WatchEntityKind.SeekerBarrier, id),
                    ReflectEvent,
                    []
                )
            );
        }
    }

    private static SeekerBarrier? Find(Level level, int id)
        => WatchEntityIDTable<SeekerBarrier>.Find(level, id);
}
