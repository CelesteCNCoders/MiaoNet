using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchPufferAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 48;
    private const byte ExplodeEvent = 1;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte FacingLeftFlag = 1 << 2;
    private const float AnchorInterval = 0.1f;

    private static readonly string[] animations =
    [
        "idle", "alert", "alerted", "explode", "unalert", "hidden", "recover",
    ];

    private readonly record struct PufferState(
        WatchPufferPhase Phase,
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 HitSpeed,
        Vector2 Scale,
        float GoneTimer,
        Vector2 LastPlayerPosition,
        float PlayerAliveFade,
        float EyeSpin
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private WatchPufferPhase phase;
        private byte flags;
        private byte animation;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, PufferState current, bool forceCurrent, float sceneTime)
        {
            if (forceCurrent || !hasState || phase != current.Phase || flags != current.Flags
                || animation != current.Animation || sceneTime >= nextAnchorTime)
            {
                state = Encode(id, current);
                phase = current.Phase;
                flags = current.Flags;
                animation = current.Animation;
                hasState = true;
                nextAnchorTime = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public PufferState State { get; set; }

        private Vector2 positionStart;
        private Vector2 positionTarget;
        private Vector2 hitSpeedStart;
        private Vector2 hitSpeedTarget;
        private Vector2 scaleStart;
        private Vector2 scaleTarget;
        private float goneTimerStart;
        private float goneTimerTarget;
        private Vector2 lastPlayerPositionStart;
        private Vector2 lastPlayerPositionTarget;
        private float playerAliveFadeStart;
        private float playerAliveFadeTarget;
        private float eyeSpinStart;
        private float eyeSpinTarget;
        private float interpolationElapsed;
        private float interpolationDuration;
        private float lastSampleTime;
        private bool hasSampleTime;

        public void Reset(PufferState state, float sceneTime)
        {
            positionStart = positionTarget = state.Position;
            hitSpeedStart = hitSpeedTarget = state.HitSpeed;
            scaleStart = scaleTarget = state.Scale;
            goneTimerStart = goneTimerTarget = state.GoneTimer;
            lastPlayerPositionStart = lastPlayerPositionTarget = state.LastPlayerPosition;
            playerAliveFadeStart = playerAliveFadeTarget = state.PlayerAliveFade;
            eyeSpinStart = eyeSpinTarget = state.EyeSpin;
            interpolationElapsed = interpolationDuration = 0f;
            lastSampleTime = sceneTime;
            hasSampleTime = true;
        }

        public void BeginInterpolation(Puffer puffer, PufferState state, float sceneTime)
        {
            float interval = hasSampleTime
                ? MathHelper.Clamp(sceneTime - lastSampleTime, AnchorInterval * 0.5f, AnchorInterval * 2f)
                : AnchorInterval;
            positionStart = puffer.Position;
            positionTarget = state.Position;
            hitSpeedStart = puffer.hitSpeed;
            hitSpeedTarget = state.HitSpeed;
            scaleStart = puffer.scale;
            scaleTarget = state.Scale;
            goneTimerStart = puffer.goneTimer;
            goneTimerTarget = state.GoneTimer;
            lastPlayerPositionStart = puffer.lastPlayerPos;
            lastPlayerPositionTarget = state.LastPlayerPosition;
            playerAliveFadeStart = puffer.playerAliveFade;
            playerAliveFadeTarget = state.PlayerAliveFade;
            eyeSpinStart = puffer.eyeSpin;
            eyeSpinTarget = state.EyeSpin;
            interpolationElapsed = 0f;
            interpolationDuration = interval;
            lastSampleTime = sceneTime;
            hasSampleTime = true;
        }

        public void Update(Puffer puffer)
        {
            if (interpolationDuration <= 0f)
                return;
            interpolationElapsed = Math.Min(
                interpolationElapsed + Engine.DeltaTime,
                interpolationDuration
            );
            float progress = interpolationElapsed / interpolationDuration;
            puffer.Position = Vector2.Lerp(positionStart, positionTarget, progress);
            puffer.hitSpeed = Vector2.Lerp(hitSpeedStart, hitSpeedTarget, progress);
            puffer.scale = Vector2.Lerp(scaleStart, scaleTarget, progress);
            puffer.goneTimer = MathHelper.Lerp(goneTimerStart, goneTimerTarget, progress);
            puffer.lastPlayerPos = Vector2.Lerp(
                lastPlayerPositionStart,
                lastPlayerPositionTarget,
                progress
            );
            puffer.playerAliveFade = MathHelper.Lerp(
                playerAliveFadeStart,
                playerAliveFadeTarget,
                progress
            );
            puffer.eyeSpin = MathHelper.Lerp(eyeSpinStart, eyeSpinTarget, progress);
        }
    }

    private static readonly WatchPufferAdapter instance = new();
    private static readonly ConditionalWeakTable<Puffer, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<Puffer, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.Puffer;

    public static void Load()
    {
        On.Celeste.Puffer.ctor_EntityData_Vector2 += Puffer_ctor;
        On.Celeste.Puffer.Update += Puffer_Update;
        On.Celeste.Puffer.GotoIdle += Puffer_GotoIdle;
        On.Celeste.Puffer.GotoHit += Puffer_GotoHit;
        On.Celeste.Puffer.GotoHitSpeed += Puffer_GotoHitSpeed;
        On.Celeste.Puffer.GotoGone += Puffer_GotoGone;
        On.Celeste.Puffer.Explode += Puffer_Explode;
        On.Celeste.Puffer.HitSpring += Puffer_HitSpring;
        On.Celeste.Puffer.OnPlayer += Puffer_OnPlayer;
        On.Celeste.Puffer.OnSquish += Puffer_OnSquish;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Puffer.OnSquish -= Puffer_OnSquish;
        On.Celeste.Puffer.OnPlayer -= Puffer_OnPlayer;
        On.Celeste.Puffer.HitSpring -= Puffer_HitSpring;
        On.Celeste.Puffer.Explode -= Puffer_Explode;
        On.Celeste.Puffer.GotoGone -= Puffer_GotoGone;
        On.Celeste.Puffer.GotoHitSpeed -= Puffer_GotoHitSpeed;
        On.Celeste.Puffer.GotoHit -= Puffer_GotoHit;
        On.Celeste.Puffer.GotoIdle -= Puffer_GotoIdle;
        On.Celeste.Puffer.Update -= Puffer_Update;
        On.Celeste.Puffer.ctor_EntityData_Vector2 -= Puffer_ctor;
        WatchEntityIDTable<Puffer>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Puffer puffer in level.Entities.OfType<Puffer>())
        {
            if (!WatchEntityIDTable<Puffer>.TryGet(puffer, room, out int id))
                continue;
            PufferState current = Capture(puffer);
            yield return syncInfo.GetValue(puffer, static _ => new()).Capture(
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
        Dictionary<int, PufferState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out PufferState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (Puffer puffer in level.Entities.OfType<Puffer>())
        {
            if (!WatchEntityIDTable<Puffer>.TryGet(puffer, room, out int id))
                continue;
            RemoteInfo applied = remoteInfo.GetValue(puffer, static _ => new());
            if (desired.Remove(id, out PufferState value))
            {
                bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset || !applied.HasState;
                if (hard)
                {
                    changed |= Apply(puffer, value);
                    applied.Reset(value, level.TimeActive);
                }
                else
                {
                    changed |= ApplyCorrection(
                        puffer,
                        applied.State,
                        value,
                        applied,
                        level.TimeActive
                    );
                }
                changed |= hard;
                applied.State = value;
                applied.HasState = true;
            }
            else if (isCompleteState)
            {
                changed |= puffer.Visible || puffer.Collidable || applied.HasState;
                puffer.Visible = false;
                puffer.Collidable = false;
                applied.HasState = false;
            }
        }

        foreach ((int id, PufferState value) in desired)
        {
            Puffer puffer = new(value.Position, false);
            WatchEntityIDTable<Puffer>.Set(puffer, room, id);
            level.Add(puffer);
            Apply(puffer, value);
            RemoteInfo applied = remoteInfo.GetValue(puffer, static _ => new());
            applied.Reset(value, level.TimeActive);
            applied.State = value;
            applied.HasState = true;
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ExplodeEvent || entityEvent.Payload.Length != 0)
            return;
        Puffer? puffer = Find(level, entityEvent.Key.EntityID);
        if (puffer is null)
            return;
        PlayRemoteExplosion(puffer, level);
    }

    private static PufferState Capture(Puffer puffer)
    {
        byte flags = 0;
        if (puffer.Visible) flags |= VisibleFlag;
        if (puffer.Collidable) flags |= CollidableFlag;
        if (puffer.facing.X < 0f) flags |= FacingLeftFlag;
        int animation = Array.IndexOf(animations, puffer.sprite.CurrentAnimationID);
        return new(
            (WatchPufferPhase)Math.Clamp((int)puffer.state, 0, (int)WatchPufferPhase.Gone),
            flags,
            (byte)Math.Max(0, animation),
            (byte)Math.Clamp(puffer.sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            puffer.Position,
            puffer.hitSpeed,
            puffer.scale,
            puffer.goneTimer,
            puffer.lastPlayerPos,
            puffer.playerAliveFade,
            puffer.eyeSpin
        );
    }

    private static WatchEntityState Encode(int id, PufferState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = (byte)state.Phase;
        payload[1] = state.Flags;
        payload[2] = state.Animation;
        payload[3] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteVector2(payload, 4, state.Position);
        WatchEntityPayloadCodec.WriteVector2(payload, 12, state.HitSpeed);
        WatchEntityPayloadCodec.WriteVector2(payload, 20, state.Scale);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.GoneTimer);
        WatchEntityPayloadCodec.WriteVector2(payload, 32, state.LastPlayerPosition);
        WatchEntityPayloadCodec.WriteSingle(payload, 40, state.PlayerAliveFade);
        WatchEntityPayloadCodec.WriteSingle(payload, 44, state.EyeSpin);
        return new(new WatchEntityKey(WatchEntityKind.Puffer, id), payload);
    }

    private static bool TryDecode(WatchEntityState state, out PufferState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.Puffer || state.Key.SubID != 0
            || payload.Length != PayloadSize || payload[0] > (byte)WatchPufferPhase.Gone
            || (payload[1] & ~0b0000_0111) != 0 || payload[2] >= animations.Length)
            return false;
        Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
        Vector2 hitSpeed = WatchEntityPayloadCodec.ReadVector2(payload, 12);
        Vector2 scale = WatchEntityPayloadCodec.ReadVector2(payload, 20);
        float goneTimer = WatchEntityPayloadCodec.ReadSingle(payload, 28);
        Vector2 lastPlayerPosition = WatchEntityPayloadCodec.ReadVector2(payload, 32);
        float playerAliveFade = WatchEntityPayloadCodec.ReadSingle(payload, 40);
        float eyeSpin = WatchEntityPayloadCodec.ReadSingle(payload, 44);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(hitSpeed.X) || !float.IsFinite(hitSpeed.Y)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(goneTimer)
            || !float.IsFinite(lastPlayerPosition.X) || !float.IsFinite(lastPlayerPosition.Y)
            || !float.IsFinite(playerAliveFade) || !float.IsFinite(eyeSpin))
            return false;
        value = new(
            (WatchPufferPhase)payload[0],
            payload[1],
            payload[2],
            payload[3],
            position,
            hitSpeed,
            scale,
            goneTimer,
            lastPlayerPosition,
            playerAliveFade,
            eyeSpin
        );
        return true;
    }

    private static bool Apply(Puffer puffer, PufferState state)
    {
        bool visible = (state.Flags & VisibleFlag) != 0;
        bool changed = puffer.Position != state.Position || puffer.Visible != visible
            || puffer.Collidable || Capture(puffer) != state;
        puffer.Position = state.Position;
        puffer.Visible = visible;
        puffer.Collidable = false;
        puffer.state = (Puffer.States)state.Phase;
        puffer.hitSpeed = state.HitSpeed;
        puffer.scale = state.Scale;
        puffer.goneTimer = state.GoneTimer;
        puffer.lastPlayerPos = state.LastPlayerPosition;
        puffer.playerAliveFade = state.PlayerAliveFade;
        puffer.eyeSpin = state.EyeSpin;
        // Puffer.Render multiplies the sprite scale component-wise by facing.
        // UnitX would therefore flatten the sprite to zero height.
        puffer.facing = new Vector2((state.Flags & FacingLeftFlag) != 0 ? -1f : 1f, 1f);
        puffer.anchorPosition = puffer.lastSpeedPosition = puffer.lastSinePosition = state.Position;
        ApplyAnimation(puffer, state);
        return changed;
    }

    private static bool ApplyCorrection(
        Puffer puffer,
        PufferState previous,
        PufferState state,
        RemoteInfo applied,
        float sceneTime
    )
    {
        if (previous.Phase != state.Phase || previous.Flags != state.Flags
            || previous.Animation != state.Animation)
        {
            bool hardChanged = Apply(puffer, state);
            applied.Reset(state, sceneTime);
            return hardChanged;
        }
        bool changed = puffer.Position != state.Position || puffer.hitSpeed != state.HitSpeed
            || puffer.scale != state.Scale || puffer.goneTimer != state.GoneTimer
            || puffer.lastPlayerPos != state.LastPlayerPosition
            || puffer.playerAliveFade != state.PlayerAliveFade || puffer.eyeSpin != state.EyeSpin;
        applied.BeginInterpolation(puffer, state, sceneTime);
        return changed;
    }

    private static void ApplyAnimation(Puffer puffer, PufferState state)
    {
        string animation = animations[state.Animation];
        if (puffer.sprite.CurrentAnimationID != animation)
            puffer.sprite.Play(animation, restart: true);
        if (puffer.sprite.CurrentAnimationTotalFrames > 0)
            puffer.sprite.SetAnimationFrame(Math.Min(state.AnimationFrame, puffer.sprite.CurrentAnimationTotalFrames - 1));
    }

    private static Puffer? Find(Level level, int id)
    {
        string room = level.Session.Level;
        return WatchEntityIDTable<Puffer>.Find(level, room, id);
    }

    private static void PlayRemoteExplosion(Puffer puffer, Level level)
    {
        Audio.Play("event:/new_content/game/10_farewell/puffer_splode", puffer.Position);
        puffer.sprite.Play("explode", restart: true);
        level.Shake(0.3f);
        level.Displacement.AddBurst(puffer.Position, 0.4f, 12f, 36f, 0.5f);
        level.Displacement.AddBurst(puffer.Position, 0.4f, 24f, 48f, 0.5f);
        level.Displacement.AddBurst(puffer.Position, 0.4f, 36f, 60f, 0.5f);
        for (float angle = 0f; angle < MathHelper.TwoPi; angle += MathHelper.Pi / 18f)
        {
            Vector2 position = puffer.Center + Calc.AngleToVector(
                angle + Calc.Random.Range(-0.034906585f, 0.034906585f),
                Calc.Random.Range(12, 18)
            );
            level.Particles.Emit(Seeker.P_Regen, position, angle);
        }
    }

    private static void Puffer_ctor(
        On.Celeste.Puffer.orig_ctor_EntityData_Vector2 orig,
        Puffer self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Puffer>.Set(self, data.Level.Name, data.ID);
    }

    private static void Puffer_Update(On.Celeste.Puffer.orig_Update orig, Puffer self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.sprite.Update();
        self.idleSine.Update();
        self.bounceWiggler.Update();
        self.inflateWiggler.Update();
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
            return;
        applied.Update(self);
    }

    private static void Puffer_GotoIdle(On.Celeste.Puffer.orig_GotoIdle orig, Puffer self)
    {
        if (!MiaoNetModule.IsWatching) orig(self);
    }

    private static void Puffer_GotoHit(
        On.Celeste.Puffer.orig_GotoHit orig,
        Puffer self,
        Vector2 from
    )
    {
        if (!MiaoNetModule.IsWatching) orig(self, from);
    }

    private static void Puffer_GotoHitSpeed(
        On.Celeste.Puffer.orig_GotoHitSpeed orig,
        Puffer self,
        Vector2 speed
    )
    {
        if (!MiaoNetModule.IsWatching) orig(self, speed);
    }

    private static void Puffer_GotoGone(On.Celeste.Puffer.orig_GotoGone orig, Puffer self)
    {
        if (!MiaoNetModule.IsWatching) orig(self);
    }

    private static void Puffer_Explode(On.Celeste.Puffer.orig_Explode orig, Puffer self)
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self);
        if (self.Scene is Level level
            && WatchEntityIDTable<Puffer>.TryGet(self, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Puffer, id), ExplodeEvent, [])
            );
    }

    private static bool Puffer_HitSpring(
        On.Celeste.Puffer.orig_HitSpring orig,
        Puffer self,
        Spring spring
    ) => !MiaoNetModule.IsWatching && orig(self, spring);

    private static void Puffer_OnPlayer(
        On.Celeste.Puffer.orig_OnPlayer orig,
        Puffer self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching) orig(self, player);
    }

    private static void Puffer_OnSquish(
        On.Celeste.Puffer.orig_OnSquish orig,
        Puffer self,
        CollisionData data
    )
    {
        if (!MiaoNetModule.IsWatching) orig(self, data);
    }
}
