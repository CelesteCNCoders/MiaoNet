using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchFinalBossShotAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 56;
    private const float AnchorInterval = 0.1f;
    private const float HardReanchorDistance = 96f;
    private const float CorrectionResponse = 18f;

    private const byte VisibleFlag = 1 << 0;
    private const byte DeadFlag = 1 << 1;
    private const byte InCameraFlag = 1 << 2;

    private readonly record struct ShotState(
        byte Flags,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 Speed,
        Vector2 Anchor,
        Vector2 Perpendicular,
        float AngleOffset,
        float CantKillTimer,
        float AppearTimer,
        float SineMultiplier,
        float ParticleDirection
    );

    private sealed class Identity
    {
        public int BossID { get; }
        public ushort SubID { get; }

        public Identity(int bossID, ushort subID)
        {
            BossID = bossID;
            SubID = subID;
        }
    }

    private sealed class Counter
    {
        public ushort Next { get; set; } = 1;
    }

    private sealed class SyncInfo
    {
        private float nextAnchorTime;
        private bool hasState;
        private WatchEntityState state;

        public WatchEntityState Capture(
            Identity identity,
            ShotState current,
            bool force,
            float sceneTime
        )
        {
            if (force || !hasState || sceneTime >= nextAnchorTime)
            {
                state = Encode(identity, current);
                hasState = true;
                nextAnchorTime = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public ShotState State { get; set; }
        public Vector2 AnchorError { get; set; }
        public Vector2 PositionError { get; set; }

        public void Reset()
        {
            AnchorError = PositionError = Vector2.Zero;
        }
    }

    private static readonly WatchFinalBossShotAdapter instance = new();
    private static readonly ConditionalWeakTable<FinalBossShot, Identity> identities = new();
    private static readonly ConditionalWeakTable<FinalBoss, Counter> counters = new();
    private static readonly ConditionalWeakTable<FinalBossShot, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<FinalBossShot, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.FinalBossShot;

    public static void Load()
    {
        On.Celeste.FinalBossShot.Init_FinalBoss_Player_float += FinalBossShot_InitPlayer;
        On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 += FinalBossShot_InitPoint;
        On.Celeste.FinalBossShot.Added += FinalBossShot_Added;
        On.Celeste.FinalBossShot.Update += FinalBossShot_Update;
        On.Celeste.FinalBossShot.OnPlayer += FinalBossShot_OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FinalBossShot.OnPlayer -= FinalBossShot_OnPlayer;
        On.Celeste.FinalBossShot.Update -= FinalBossShot_Update;
        On.Celeste.FinalBossShot.Added -= FinalBossShot_Added;
        On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 -= FinalBossShot_InitPoint;
        On.Celeste.FinalBossShot.Init_FinalBoss_Player_float -= FinalBossShot_InitPlayer;
        identities.Clear();
        counters.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (FinalBossShot shot in level.Entities.OfType<FinalBossShot>())
        {
            if (!identities.TryGetValue(shot, out Identity? identity))
                continue;
            yield return syncInfo.GetValue(shot, static _ => new()).Capture(
                identity,
                Capture(shot),
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
        Dictionary<(int BossID, ushort SubID), ShotState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out ShotState value)
                || !desired.TryAdd((state.Key.EntityID, state.Key.SubID), value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        Dictionary<(int, ushort), FinalBossShot> existing = level.Entities
            .OfType<FinalBossShot>()
            .Select(shot => (
                Shot: shot,
                HasIdentity: identities.TryGetValue(shot, out Identity? identity),
                Identity: identity
            ))
            .Where(item => item.HasIdentity && item.Identity is not null)
            .ToDictionary(
                item => (item.Identity!.BossID, item.Identity.SubID),
                item => item.Shot
            );

        foreach (((int bossID, ushort subID), ShotState state) in desired)
        {
            if (!existing.Remove((bossID, subID), out FinalBossShot? shot))
            {
                FinalBoss? boss = FindBoss(level, bossID);
                if (boss is null)
                    continue;
                shot = new FinalBossShot();
                identities.AddOrUpdate(shot, new Identity(bossID, subID));
                shot.boss = boss;
                shot.level = level;
                level.Add(shot);
                changed = true;
            }
            changed |= ApplyAnchor(shot, state);
        }

        if (isCompleteState)
        {
            foreach (FinalBossShot shot in existing.Values)
            {
                changed = true;
                shot.Collidable = false;
                shot.Visible = false;
                shot.RemoveSelf();
                remoteInfo.GetValue(shot, static _ => new()).HasState = false;
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }

    private static ShotState Capture(FinalBossShot shot)
    {
        byte flags = 0;
        if (shot.Visible) flags |= VisibleFlag;
        if (shot.dead) flags |= DeadFlag;
        if (shot.hasBeenInCamera) flags |= InCameraFlag;
        return new(
            flags,
            (byte)Math.Clamp(shot.sprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            shot.Position,
            shot.speed,
            shot.anchor,
            shot.perp,
            shot.angleOffset,
            shot.cantKillTimer,
            shot.appearTimer,
            shot.sineMult,
            shot.particleDir
        );
    }

    private static WatchEntityState Encode(Identity identity, ShotState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        payload[1] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.Speed.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.Speed.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.Anchor.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.Anchor.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.Perpendicular.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.Perpendicular.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.AngleOffset);
        WatchEntityPayloadCodec.WriteSingle(payload, 40, state.CantKillTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 44, state.AppearTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 48, state.SineMultiplier);
        WatchEntityPayloadCodec.WriteSingle(payload, 52, state.ParticleDirection);
        return new(
            new WatchEntityKey(WatchEntityKind.FinalBossShot, identity.BossID, identity.SubID),
            payload
        );
    }

    private static bool TryDecode(WatchEntityState state, out ShotState value)
    {
        value = default;
        ReadOnlySpan<byte> p = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.FinalBossShot || state.Key.SubID == 0
            || p.Length != PayloadSize || (p[0] & ~0b0000_0111) != 0 || p[3] != 0)
            return false;
        float[] values = new float[13];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = WatchEntityPayloadCodec.ReadSingle(p, 4 + index * 4);
            if (!float.IsFinite(values[index]))
                return false;
        }
        value = new(
            p[0],
            p[1],
            new(values[0], values[1]),
            new(values[2], values[3]),
            new(values[4], values[5]),
            new(values[6], values[7]),
            values[8],
            values[9],
            values[10],
            values[11],
            values[12]
        );
        return true;
    }

    private static bool ApplyAnchor(FinalBossShot shot, ShotState state)
    {
        RemoteInfo applied = remoteInfo.GetValue(shot, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || ((applied.State.Flags ^ state.Flags) & DeadFlag) != 0
            || Vector2.DistanceSquared(shot.Position, state.Position)
                >= HardReanchorDistance * HardReanchorDistance;
        bool changed = !applied.HasState || applied.State != state;
        ApplyPresentation(shot, state);
        if (hard)
        {
            shot.Position = state.Position;
            shot.anchor = state.Anchor;
            applied.Reset();
        }
        else
        {
            applied.PositionError += state.Position - shot.Position;
            applied.AnchorError += state.Anchor - shot.anchor;
        }
        applied.State = state;
        applied.HasState = true;
        return changed;
    }

    private static void ApplyPresentation(FinalBossShot shot, ShotState state)
    {
        shot.Visible = (state.Flags & VisibleFlag) != 0;
        shot.Collidable = false;
        shot.dead = (state.Flags & DeadFlag) != 0;
        shot.hasBeenInCamera = (state.Flags & InCameraFlag) != 0;
        shot.speed = state.Speed;
        shot.perp = state.Perpendicular;
        shot.angleOffset = state.AngleOffset;
        shot.cantKillTimer = state.CantKillTimer;
        shot.appearTimer = state.AppearTimer;
        shot.sineMult = state.SineMultiplier;
        shot.particleDir = state.ParticleDirection;
        if (shot.sprite is not null && shot.sprite.CurrentAnimationTotalFrames > 0)
            shot.sprite.SetAnimationFrame(Math.Min(state.AnimationFrame, shot.sprite.CurrentAnimationTotalFrames - 1));
    }

    private static void AssignIdentity(FinalBossShot shot, FinalBoss boss)
    {
        if (boss.Scene is not Level level
            || !WatchEntityIDTable<FinalBoss>.TryGet(boss, level.Session.Level, out int bossID))
            return;
        Counter counter = counters.GetValue(boss, static _ => new());
        ushort subID;
        do
            subID = counter.Next++;
        while (subID == 0);
        syncInfo.Remove(shot);
        remoteInfo.Remove(shot);
        identities.AddOrUpdate(shot, new Identity(bossID, subID));
    }

    private static FinalBoss? FindBoss(Level level, int bossID)
        => level.Entities.OfType<FinalBoss>().FirstOrDefault(boss =>
            WatchEntityIDTable<FinalBoss>.TryGet(boss, level.Session.Level, out int candidate)
            && candidate == bossID
        );

    private static FinalBossShot FinalBossShot_InitPlayer(
        On.Celeste.FinalBossShot.orig_Init_FinalBoss_Player_float orig,
        FinalBossShot self,
        FinalBoss boss,
        Player target,
        float angleOffset
    )
    {
        FinalBossShot result = orig(self, boss, target, angleOffset);
        if (!MiaoNetModule.IsWatching)
            AssignIdentity(self, boss);
        return result;
    }

    private static FinalBossShot FinalBossShot_InitPoint(
        On.Celeste.FinalBossShot.orig_Init_FinalBoss_Vector2 orig,
        FinalBossShot self,
        FinalBoss boss,
        Vector2 target
    )
    {
        FinalBossShot result = orig(self, boss, target);
        if (!MiaoNetModule.IsWatching)
            AssignIdentity(self, boss);
        return result;
    }

    private static void FinalBossShot_Added(
        On.Celeste.FinalBossShot.orig_Added orig,
        FinalBossShot self,
        Scene scene
    )
    {
        if (!MiaoNetModule.IsWatching || self.boss is null)
        {
            orig(self, scene);
            return;
        }
        WatchFinalBossAdapter.EnsureBossSprite(self.boss);
        bool moving = self.boss.Moving;
        self.boss.Moving = false;
        try
        {
            orig(self, scene);
        }
        finally
        {
            self.boss.Moving = moving;
        }
        self.Collidable = false;
    }

    private static void FinalBossShot_Update(
        On.Celeste.FinalBossShot.orig_Update orig,
        FinalBossShot self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.Collidable = false;
        self.Components.Update();
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
            return;

        float dt = Engine.DeltaTime;
        if (self.appearTimer > 0f)
        {
            if (self.boss is not null
                && WatchFinalBossAdapter.EnsureBossSprite(self.boss))
                self.Position = self.anchor = self.boss.ShotOrigin;
            self.appearTimer -= dt;
        }
        else
        {
            self.cantKillTimer -= dt;
            self.anchor += self.speed * dt;
            self.Position = self.anchor + self.perp * self.sineMult * self.sine.Value * 3f;
            self.sineMult = Calc.Approach(self.sineMult, 1f, 2f * dt);
        }

        float response = 1f - MathF.Exp(-CorrectionResponse * dt);
        Vector2 anchorCorrection = applied.AnchorError * response;
        Vector2 positionCorrection = applied.PositionError * response;
        self.anchor += anchorCorrection;
        self.Position += positionCorrection;
        applied.AnchorError -= anchorCorrection;
        applied.PositionError -= positionCorrection;
    }

    private static void FinalBossShot_OnPlayer(
        On.Celeste.FinalBossShot.orig_OnPlayer orig,
        FinalBossShot self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }
}

internal sealed class WatchFinalBossBeamAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 28;
    private const float AnchorInterval = 0.1f;
    private const float AngleCorrectionResponse = 18f;
    private const byte FireEvent = 1;

    private static readonly string[] animations = ["charge", "lock", "shoot"];

    private readonly record struct BeamState(
        WatchFinalBossBeamPhase Phase,
        byte Animation,
        byte AnimationFrame,
        float Angle,
        float ChargeTimer,
        float FollowTimer,
        float ActiveTimer,
        float BeamAlpha,
        float SideFadeAlpha
    );

    private sealed class Identity
    {
        public int BossID { get; }
        public ushort SubID { get; }

        public Identity(int bossID, ushort subID)
        {
            BossID = bossID;
            SubID = subID;
        }
    }

    private sealed class Counter
    {
        public ushort Next { get; set; } = 1;
    }

    private sealed class SyncInfo
    {
        private float nextAnchorTime;
        private bool hasState;
        private WatchEntityState state;

        public WatchEntityState Capture(Identity identity, BeamState current, bool force, float time)
        {
            if (force || !hasState || time >= nextAnchorTime)
            {
                state = Encode(identity, current);
                hasState = true;
                nextAnchorTime = time + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public BeamState State { get; set; }
        public float AngleError { get; set; }
    }

    private static readonly WatchFinalBossBeamAdapter instance = new();
    private static readonly ConditionalWeakTable<FinalBossBeam, Identity> identities = new();
    private static readonly ConditionalWeakTable<FinalBoss, Counter> counters = new();
    private static readonly ConditionalWeakTable<FinalBossBeam, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<FinalBossBeam, RemoteInfo> remoteInfo = new();
    private static bool replayingFireEvent;

    public WatchEntityKind Kind => WatchEntityKind.FinalBossBeam;

    public static void Load()
    {
        On.Celeste.FinalBossBeam.Init += FinalBossBeam_Init;
        On.Celeste.FinalBossBeam.Added += FinalBossBeam_Added;
        On.Celeste.FinalBossBeam.Update += FinalBossBeam_Update;
        On.Celeste.FinalBossBeam.PlayerCollideCheck += FinalBossBeam_PlayerCollideCheck;
        On.Celeste.FinalBossBeam.DissipateParticles += FinalBossBeam_DissipateParticles;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FinalBossBeam.DissipateParticles -= FinalBossBeam_DissipateParticles;
        On.Celeste.FinalBossBeam.PlayerCollideCheck -= FinalBossBeam_PlayerCollideCheck;
        On.Celeste.FinalBossBeam.Update -= FinalBossBeam_Update;
        On.Celeste.FinalBossBeam.Added -= FinalBossBeam_Added;
        On.Celeste.FinalBossBeam.Init -= FinalBossBeam_Init;
        identities.Clear();
        counters.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        replayingFireEvent = false;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (FinalBossBeam beam in level.Entities.OfType<FinalBossBeam>())
        {
            if (!identities.TryGetValue(beam, out Identity? identity))
                continue;
            yield return syncInfo.GetValue(beam, static _ => new()).Capture(
                identity,
                Capture(beam),
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
        Dictionary<(int BossID, ushort SubID), BeamState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out BeamState value)
                || !desired.TryAdd((state.Key.EntityID, state.Key.SubID), value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        Dictionary<(int, ushort), FinalBossBeam> existing = level.Entities
            .OfType<FinalBossBeam>()
            .Select(beam => (
                Beam: beam,
                HasIdentity: identities.TryGetValue(beam, out Identity? identity),
                Identity: identity
            ))
            .Where(item => item.HasIdentity && item.Identity is not null)
            .ToDictionary(
                item => (item.Identity!.BossID, item.Identity.SubID),
                item => item.Beam
            );

        foreach (((int bossID, ushort subID), BeamState state) in desired)
        {
            if (!existing.Remove((bossID, subID), out FinalBossBeam? beam))
            {
                FinalBoss? boss = FindBoss(level, bossID);
                if (boss is null)
                    continue;
                beam = new FinalBossBeam
                {
                    boss = boss,
                    player = null!,
                };
                identities.AddOrUpdate(beam, new Identity(bossID, subID));
                level.Add(beam);
                changed = true;
            }
            changed |= ApplyAnchor(beam, state);
        }

        if (isCompleteState)
        {
            foreach (FinalBossBeam beam in existing.Values)
            {
                changed = true;
                beam.Visible = false;
                beam.RemoveSelf();
                remoteInfo.GetValue(beam, static _ => new()).HasState = false;
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != FireEvent || entityEvent.Payload.Length != 0)
            return;
        FinalBossBeam? beam = level.Entities.OfType<FinalBossBeam>().FirstOrDefault(candidate =>
            identities.TryGetValue(candidate, out Identity? identity)
            && identity.BossID == entityEvent.Key.EntityID
            && identity.SubID == entityEvent.Key.SubID
        );
        if (beam is null)
            return;
        replayingFireEvent = true;
        try
        {
            beam.DissipateParticles();
        }
        finally
        {
            replayingFireEvent = false;
        }
    }

    private static BeamState Capture(FinalBossBeam beam)
    {
        WatchFinalBossBeamPhase phase = beam.chargeTimer > 0f
            ? WatchFinalBossBeamPhase.Charging
            : beam.activeTimer > 0f
                ? WatchFinalBossBeamPhase.Active
                : WatchFinalBossBeamPhase.Dissipating;
        int animation = Array.IndexOf(animations, beam.beamSprite?.CurrentAnimationID);
        return new(
            phase,
            (byte)(animation >= 0 ? animation : 0),
            (byte)Math.Clamp(beam.beamSprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            beam.angle,
            beam.chargeTimer,
            beam.followTimer,
            beam.activeTimer,
            beam.beamAlpha,
            beam.sideFadeAlpha
        );
    }

    private static WatchEntityState Encode(Identity identity, BeamState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = (byte)state.Phase;
        payload[1] = state.Animation;
        payload[2] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Angle);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.ChargeTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.FollowTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.ActiveTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.BeamAlpha);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.SideFadeAlpha);
        return new(
            new WatchEntityKey(WatchEntityKind.FinalBossBeam, identity.BossID, identity.SubID),
            payload
        );
    }

    private static bool TryDecode(WatchEntityState state, out BeamState value)
    {
        value = default;
        ReadOnlySpan<byte> p = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.FinalBossBeam || state.Key.SubID == 0
            || p.Length != PayloadSize
            || p[0] > (byte)WatchFinalBossBeamPhase.Dissipating
            || p[1] >= animations.Length || p[3] != 0)
            return false;
        float[] values = new float[6];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = WatchEntityPayloadCodec.ReadSingle(p, 4 + index * 4);
            if (!float.IsFinite(values[index]))
                return false;
        }
        value = new(
            (WatchFinalBossBeamPhase)p[0],
            p[1],
            p[2],
            values[0],
            values[1],
            values[2],
            values[3],
            values[4],
            values[5]
        );
        return true;
    }

    private static bool ApplyAnchor(FinalBossBeam beam, BeamState state)
    {
        RemoteInfo applied = remoteInfo.GetValue(beam, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState || applied.State.Phase != state.Phase;
        bool changed = !applied.HasState || applied.State != state;
        if (hard)
        {
            beam.angle = state.Angle;
            applied.AngleError = 0f;
        }
        else
        {
            applied.AngleError = Calc.AngleDiff(beam.angle, state.Angle);
        }
        beam.chargeTimer = state.ChargeTimer;
        beam.followTimer = state.FollowTimer;
        beam.activeTimer = state.ActiveTimer;
        beam.beamAlpha = state.BeamAlpha;
        beam.sideFadeAlpha = state.SideFadeAlpha;
        beam.player = null!;
        ApplyAnimation(beam, state);
        applied.State = state;
        applied.HasState = true;
        return changed;
    }

    private static void ApplyAnimation(FinalBossBeam beam, BeamState state)
    {
        string animation = animations[state.Animation];
        if (beam.beamSprite.CurrentAnimationID != animation)
            beam.beamSprite.Play(animation, restart: true);
        if (beam.beamSprite.CurrentAnimationTotalFrames > 0)
            beam.beamSprite.SetAnimationFrame(Math.Min(state.AnimationFrame, beam.beamSprite.CurrentAnimationTotalFrames - 1));
        if (state.Phase == WatchFinalBossBeamPhase.Active
            && beam.beamStartSprite.CurrentAnimationID != "shoot")
            beam.beamStartSprite.Play("shoot", restart: true);
    }

    private static void AssignIdentity(FinalBossBeam beam, FinalBoss boss)
    {
        if (boss.Scene is not Level level
            || !WatchEntityIDTable<FinalBoss>.TryGet(boss, level.Session.Level, out int bossID))
            return;
        Counter counter = counters.GetValue(boss, static _ => new());
        ushort subID;
        do
            subID = counter.Next++;
        while (subID == 0);
        syncInfo.Remove(beam);
        remoteInfo.Remove(beam);
        identities.AddOrUpdate(beam, new Identity(bossID, subID));
    }

    private static FinalBoss? FindBoss(Level level, int bossID)
        => level.Entities.OfType<FinalBoss>().FirstOrDefault(boss =>
            WatchEntityIDTable<FinalBoss>.TryGet(boss, level.Session.Level, out int candidate)
            && candidate == bossID
        );

    private static FinalBossBeam FinalBossBeam_Init(
        On.Celeste.FinalBossBeam.orig_Init orig,
        FinalBossBeam self,
        FinalBoss boss,
        Player target
    )
    {
        FinalBossBeam result = orig(self, boss, target);
        if (!MiaoNetModule.IsWatching)
            AssignIdentity(self, boss);
        return result;
    }

    private static void FinalBossBeam_Added(
        On.Celeste.FinalBossBeam.orig_Added orig,
        FinalBossBeam self,
        Scene scene
    )
    {
        if (!MiaoNetModule.IsWatching || self.boss is null)
        {
            orig(self, scene);
            return;
        }
        WatchFinalBossAdapter.EnsureBossSprite(self.boss);
        bool moving = self.boss.Moving;
        self.boss.Moving = false;
        try
        {
            orig(self, scene);
        }
        finally
        {
            self.boss.Moving = moving;
        }
        self.player = null!;
    }

    private static void FinalBossBeam_Update(
        On.Celeste.FinalBossBeam.orig_Update orig,
        FinalBossBeam self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (self.boss is not null)
            WatchFinalBossAdapter.EnsureBossSprite(self.boss);
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.Components.Update();
        self.player = null!;
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
            return;

        float dt = Engine.DeltaTime;
        float response = 1f - MathF.Exp(-AngleCorrectionResponse * dt);
        float correction = applied.AngleError * response;
        self.angle += correction;
        applied.AngleError -= correction;
        if (self.chargeTimer > 0f)
        {
            self.chargeTimer -= dt;
            self.followTimer -= dt;
            self.beamAlpha = Calc.Approach(self.beamAlpha, 1f, 2f * dt);
            self.sideFadeAlpha = Calc.Approach(self.sideFadeAlpha, 1f, dt);
        }
        else if (self.activeTimer > 0f)
        {
            self.activeTimer -= dt;
            self.sideFadeAlpha = Calc.Approach(self.sideFadeAlpha, 0f, 8f * dt);
        }
    }

    private static void FinalBossBeam_PlayerCollideCheck(
        On.Celeste.FinalBossBeam.orig_PlayerCollideCheck orig,
        FinalBossBeam self
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self);
    }

    private static void FinalBossBeam_DissipateParticles(
        On.Celeste.FinalBossBeam.orig_DissipateParticles orig,
        FinalBossBeam self
    )
    {
        if (MiaoNetModule.IsWatching && !replayingFireEvent)
            return;
        orig(self);
        if (MiaoNetModule.IsWatching || self.Scene is not Level level
            || !identities.TryGetValue(self, out Identity? identity))
            return;
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(
                new WatchEntityKey(WatchEntityKind.FinalBossBeam, identity.BossID, identity.SubID),
                FireEvent,
                []
            )
        );
    }
}
