using MiaoNet.Shared;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Keeps Angry Oshiro's original renderer and locally advances its deterministic
/// phase motion while the watched client remains authoritative for phase changes.
/// The watcher never runs the vanilla Player collision, time-rate, anxiety, or AI
/// callbacks.
/// </summary>
internal sealed class WatchAngryOshiroAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 40;
    private const float AnchorInterval = 0.1f;
    private const float HardReanchorDistance = 96f;
    private const float CorrectionResponse = 18f;
    private const float PlayerCenterYOffset = -5.5f;

    private const byte VisibleFlag = 1 << 0;
    private const byte LightningVisibleFlag = 1 << 1;
    private const byte RespawnFlag = 1 << 2;
    private const byte LeavingFlag = 1 << 3;
    private const byte FromCutsceneFlag = 1 << 4;
    private const byte EaseBackFlag = 1 << 5;
    private const byte ValidFlags = VisibleFlag | LightningVisibleFlag | RespawnFlag
        | LeavingFlag | FromCutsceneFlag | EaseBackFlag;
    private const byte HardLifecycleFlags = VisibleFlag | RespawnFlag | FromCutsceneFlag;

    private static readonly string[] animations =
    [
        "transformStart",
        "transformCharge",
        "transformFinish",
        "transformBack",
        "respawn",
        "idle",
        "charge",
        "dash",
        "hurt",
    ];

    private readonly record struct OshiroState(
        WatchAngryOshiroPhase Phase,
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        float CameraXOffset,
        float AttackSpeed,
        float YApproachSpeed,
        Vector2 Scale,
        int Depth,
        byte LightningFrame
    );

    private readonly record struct SyncSignature(
        WatchAngryOshiroPhase Phase,
        byte Flags,
        byte Animation
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private SyncSignature signature;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(OshiroState current, bool forceCurrent, float sceneTime)
        {
            SyncSignature currentSignature = new(current.Phase, current.Flags, current.Animation);
            if (forceCurrent || !hasState || currentSignature != signature
                || sceneTime >= nextAnchorTime)
            {
                state = Encode(current);
                signature = currentSignature;
                hasState = true;
                nextAnchorTime = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public OshiroState State { get; set; }
        public Vector2 PositionError { get; set; }
        public float CameraOffsetError { get; set; }
        public float AttackSpeedError { get; set; }
        public float YApproachSpeedError { get; set; }
        public Vector2 ScaleError { get; set; }

        public void ResetErrors()
        {
            PositionError = Vector2.Zero;
            CameraOffsetError = 0f;
            AttackSpeedError = 0f;
            YApproachSpeedError = 0f;
            ScaleError = Vector2.Zero;
        }
    }

    private static readonly WatchAngryOshiroAdapter instance = new();
    private static readonly ConditionalWeakTable<AngryOshiro, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<AngryOshiro, RemoteInfo> remoteInfo = new();
    private static Vector2 remotePlayerPosition;
    private static bool hasRemotePlayerPosition;

    public WatchEntityKind Kind => WatchEntityKind.AngryOshiro;

    public static void Load()
    {
        On.Celeste.AngryOshiro.Added += AngryOshiro_Added;
        On.Celeste.AngryOshiro.Update += AngryOshiro_Update;
        On.Celeste.AngryOshiro.OnPlayer += AngryOshiro_OnPlayer;
        On.Celeste.AngryOshiro.OnPlayerBounce += AngryOshiro_OnPlayerBounce;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.AngryOshiro.OnPlayerBounce -= AngryOshiro_OnPlayerBounce;
        On.Celeste.AngryOshiro.OnPlayer -= AngryOshiro_OnPlayer;
        On.Celeste.AngryOshiro.Update -= AngryOshiro_Update;
        On.Celeste.AngryOshiro.Added -= AngryOshiro_Added;
        syncInfo.Clear();
        remoteInfo.Clear();
        ResetRemotePlayerState();
    }

    public static void RecordRemotePlayerFrame(PlayerStateDelta delta)
    {
        if (!float.IsFinite(delta.Position.X) || !float.IsFinite(delta.Position.Y))
            return;
        remotePlayerPosition = delta.Position;
        hasRemotePlayerPosition = true;
    }

    public static void ResetRemotePlayerState()
    {
        remotePlayerPosition = Vector2.Zero;
        hasRemotePlayerPosition = false;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        AngryOshiro? oshiro = level.Entities.OfType<AngryOshiro>().FirstOrDefault();
        if (oshiro is null)
            yield break;

        OshiroState current = Capture(oshiro);
        yield return syncInfo.GetValue(oshiro, static _ => new()).Capture(
            current,
            WatchEntitySyncRegistry.IsCapturingCurrentState,
            level.TimeActive
        );
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        OshiroState desired = default;
        bool hasDesired = false;
        foreach (WatchEntityState state in states)
        {
            if (hasDesired || !TryDecode(state, out desired))
                return WatchEntityApplyResult.None;
            hasDesired = true;
        }

        AngryOshiro[] existing = level.Entities.OfType<AngryOshiro>().ToArray();
        if (!hasDesired)
        {
            if (!isCompleteState)
                return WatchEntityApplyResult.None;
            bool removed = false;
            foreach (AngryOshiro oshiro in existing)
            {
                DisableLocalBehavior(oshiro);
                oshiro.Visible = false;
                oshiro.RemoveSelf();
                removed = true;
            }
            return removed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
        }

        AngryOshiro target;
        if (existing.Length == 0)
        {
            target = new AngryOshiro(
                desired.Position,
                (desired.Flags & FromCutsceneFlag) != 0
            );
            level.Add(target);
        }
        else
        {
            target = existing[0];
            foreach (AngryOshiro duplicate in existing.Skip(1))
            {
                DisableLocalBehavior(duplicate);
                duplicate.Visible = false;
                duplicate.RemoveSelf();
            }
        }

        RemoteInfo applied = remoteInfo.GetValue(target, static _ => new());
        bool phaseChanged = applied.HasState && applied.State.Phase != desired.Phase;
        bool lifecycleChanged = applied.HasState
            && ((applied.State.Flags ^ desired.Flags) & HardLifecycleFlags) != 0;
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || phaseChanged
            || lifecycleChanged
            || Vector2.DistanceSquared(target.Position, desired.Position)
                > HardReanchorDistance * HardReanchorDistance;

        ApplyAnchor(target, desired, applied, hard);
        applied.State = desired;
        applied.HasState = true;
        return WatchEntityApplyResult.SceneChanged;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }

    private static OshiroState Capture(AngryOshiro oshiro)
    {
        byte flags = 0;
        if (oshiro.Visible) flags |= VisibleFlag;
        if (oshiro.lightningVisible) flags |= LightningVisibleFlag;
        if (oshiro.doRespawnAnim) flags |= RespawnFlag;
        if (oshiro.leaving) flags |= LeavingFlag;
        if (oshiro.fromCutscene) flags |= FromCutsceneFlag;
        if (oshiro.easeBackFromRightEdge) flags |= EaseBackFlag;

        int animation = Array.IndexOf(animations, oshiro.Sprite.CurrentAnimationID);
        if (animation < 0)
            animation = 5;
        int phase = Math.Clamp(
            oshiro.state.State,
            (int)WatchAngryOshiroPhase.Chase,
            (int)WatchAngryOshiroPhase.Hurt
        );
        return new(
            (WatchAngryOshiroPhase)phase,
            flags,
            (byte)animation,
            (byte)Math.Clamp(oshiro.Sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            oshiro.Position,
            oshiro.cameraXOffset,
            oshiro.attackSpeed,
            oshiro.yApproachSpeed,
            oshiro.Sprite.Scale,
            oshiro.Depth,
            (byte)Math.Clamp(oshiro.lightning.CurrentAnimationFrame, 0, 6)
        );
    }

    private static WatchEntityState Encode(OshiroState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = (byte)state.Phase;
        payload[1] = state.Flags;
        payload[2] = state.Animation;
        payload[3] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.CameraXOffset);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.AttackSpeed);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.YApproachSpeed);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.Scale.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.Scale.Y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(32), state.Depth);
        payload[36] = state.LightningFrame;
        return new(new WatchEntityKey(WatchEntityKind.AngryOshiro, 0), payload);
    }

    private static bool TryDecode(WatchEntityState state, out OshiroState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.AngryOshiro
            || state.Key.EntityID != 0
            || state.Key.SubID != 0
            || payload.Length != PayloadSize
            || payload[0] > (byte)WatchAngryOshiroPhase.Hurt
            || (payload[1] & ~ValidFlags) != 0
            || payload[2] >= animations.Length
            || payload[36] > 6
            || payload[37] != 0 || payload[38] != 0 || payload[39] != 0)
            return false;

        Vector2 position = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 4),
            WatchEntityPayloadCodec.ReadSingle(payload, 8)
        );
        float cameraXOffset = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float attackSpeed = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float yApproachSpeed = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        Vector2 scale = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 24),
            WatchEntityPayloadCodec.ReadSingle(payload, 28)
        );
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(cameraXOffset) || !float.IsFinite(attackSpeed)
            || !float.IsFinite(yApproachSpeed)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y))
            return false;

        value = new(
            (WatchAngryOshiroPhase)payload[0],
            payload[1],
            payload[2],
            payload[3],
            position,
            cameraXOffset,
            attackSpeed,
            yApproachSpeed,
            scale,
            BinaryPrimitives.ReadInt32LittleEndian(payload[32..]),
            payload[36]
        );
        return true;
    }

    private static void ApplyAnchor(
        AngryOshiro oshiro,
        OshiroState state,
        RemoteInfo applied,
        bool hard
    )
    {
        DisableLocalBehavior(oshiro);
        bool animationChanged = !applied.HasState || applied.State.Animation != state.Animation;
        bool lightningBegan = (state.Flags & LightningVisibleFlag) != 0
            && (!applied.HasState
                || (applied.State.Flags & LightningVisibleFlag) == 0);

        oshiro.Visible = (state.Flags & VisibleFlag) != 0;
        oshiro.Collidable = false;
        oshiro.lightningVisible = (state.Flags & LightningVisibleFlag) != 0;
        oshiro.doRespawnAnim = (state.Flags & RespawnFlag) != 0;
        oshiro.leaving = (state.Flags & LeavingFlag) != 0;
        oshiro.fromCutscene = (state.Flags & FromCutsceneFlag) != 0;
        oshiro.easeBackFromRightEdge = (state.Flags & EaseBackFlag) != 0;
        oshiro.Depth = state.Depth;

        if (hard)
        {
            oshiro.Position = state.Position;
            oshiro.cameraXOffset = state.CameraXOffset;
            oshiro.attackSpeed = state.AttackSpeed;
            oshiro.yApproachSpeed = state.YApproachSpeed;
            oshiro.Sprite.Scale = state.Scale;
            applied.ResetErrors();
        }
        else
        {
            applied.PositionError = state.Position - oshiro.Position;
            applied.CameraOffsetError = state.CameraXOffset - oshiro.cameraXOffset;
            applied.AttackSpeedError = state.AttackSpeed - oshiro.attackSpeed;
            applied.YApproachSpeedError = state.YApproachSpeed - oshiro.yApproachSpeed;
            applied.ScaleError = state.Scale - oshiro.Sprite.Scale;
        }

        ApplyAnimation(oshiro.Sprite, state.Animation, state.AnimationFrame, hard || animationChanged);
        if (lightningBegan || hard && oshiro.lightningVisible)
        {
            oshiro.lightning.Play("once", restart: true);
            if (oshiro.lightning.CurrentAnimationTotalFrames > 0)
            {
                oshiro.lightning.SetAnimationFrame(Math.Min(
                    state.LightningFrame,
                    oshiro.lightning.CurrentAnimationTotalFrames - 1
                ));
            }
        }
    }

    private static void ApplyAnimation(
        Sprite sprite,
        byte animation,
        byte frame,
        bool alignFrame
    )
    {
        string id = animations[animation];
        if (sprite.CurrentAnimationID != id)
            sprite.Play(id, restart: true);
        if (alignFrame && sprite.CurrentAnimationTotalFrames > 0)
        {
            sprite.SetAnimationFrame(Math.Min(
                frame,
                sprite.CurrentAnimationTotalFrames - 1
            ));
        }
    }

    private static void DisableLocalBehavior(AngryOshiro oshiro)
    {
        oshiro.Collidable = false;
        oshiro.canControlTimeRate = false;
        oshiro.state.Active = false;
        foreach (PlayerCollider collider in oshiro.Components.GetAll<PlayerCollider>())
            collider.Active = false;
        foreach (TransitionListener listener in oshiro.Components.GetAll<TransitionListener>())
            listener.Active = false;
    }

    private static void AngryOshiro_Added(
        On.Celeste.AngryOshiro.orig_Added orig,
        AngryOshiro self,
        Scene scene
    )
    {
        orig(self, scene);
        if (MiaoNetModule.IsWatching
            && remoteInfo.TryGetValue(self, out RemoteInfo? applied)
            && applied.HasState)
        {
            ApplyAnchor(self, applied.State, applied, hard: true);
        }
    }

    private static void AngryOshiro_Update(
        On.Celeste.AngryOshiro.orig_Update orig,
        AngryOshiro self
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

        OshiroState state = applied.State;
        self.Visible = (state.Flags & VisibleFlag) != 0;
        self.Collidable = false;
        self.lightningVisible = (state.Flags & LightningVisibleFlag) != 0;
        self.Depth = state.Depth;

        // StateMachine and PlayerCollider components are inactive, so this only
        // advances the original Sprite, lightning, light, sine, shaker, and audio
        // components once per rendered frame.
        self.Components.Update();

        float deltaTime = Engine.DeltaTime;
        bool cameraRelative = false;
        switch (state.Phase)
        {
            case WatchAngryOshiroPhase.Chase:
                self.cameraXOffset = Calc.Approach(
                    self.cameraXOffset,
                    20f,
                    80f * deltaTime
                );
                self.yApproachSpeed = Calc.Approach(
                    self.yApproachSpeed,
                    100f,
                    300f * deltaTime
                );
                if (self.Sprite.CurrentAnimationID != "respawn")
                {
                    self.CenterY = Calc.Approach(
                        self.CenterY,
                        GetRemoteTargetY(self.level, state.Position.Y),
                        self.yApproachSpeed * deltaTime
                    );
                }
                cameraRelative = true;
                break;

            case WatchAngryOshiroPhase.ChargeUp:
                self.cameraXOffset = Calc.Approach(
                    self.cameraXOffset,
                    0f,
                    40f * deltaTime
                );
                self.CenterY = Calc.Approach(
                    self.CenterY,
                    GetRemoteTargetY(self.level, state.Position.Y),
                    30f * deltaTime
                );
                cameraRelative = true;
                break;

            case WatchAngryOshiroPhase.Attack:
                self.X += self.attackSpeed * deltaTime;
                self.attackSpeed = Calc.Approach(
                    self.attackSpeed,
                    500f,
                    2000f * deltaTime
                );
                if (self.Scene?.OnInterval(0.05f) == true)
                    TrailManager.Add(self, Color.Red * 0.6f, 0.5f, frozenUpdate: false);
                break;

            case WatchAngryOshiroPhase.Hurt:
                self.X += 100f * deltaTime;
                self.Y += 200f * deltaTime;
                break;
        }

        float correction = 1f - MathF.Exp(-CorrectionResponse * deltaTime);
        self.cameraXOffset += applied.CameraOffsetError * correction;
        applied.CameraOffsetError *= 1f - correction;
        self.attackSpeed += applied.AttackSpeedError * correction;
        applied.AttackSpeedError *= 1f - correction;
        self.yApproachSpeed += applied.YApproachSpeedError * correction;
        applied.YApproachSpeedError *= 1f - correction;
        self.Sprite.Scale += applied.ScaleError * correction;
        applied.ScaleError *= 1f - correction;

        if (cameraRelative)
        {
            self.X = self.level.Camera.Left + self.cameraXOffset;
            applied.PositionError = new Vector2(0f, applied.PositionError.Y);
        }
        self.Position += applied.PositionError * correction;
        applied.PositionError *= 1f - correction;

        self.Sprite.Scale.X = Calc.Approach(self.Sprite.Scale.X, 1f, 0.6f * deltaTime);
        self.Sprite.Scale.Y = Calc.Approach(self.Sprite.Scale.Y, 1f, 0.6f * deltaTime);
    }

    private static float GetRemoteTargetY(Level level, float fallback)
    {
        if (!hasRemotePlayerPosition)
            return fallback;
        return MathHelper.Clamp(
            remotePlayerPosition.Y + PlayerCenterYOffset,
            level.Bounds.Top + 8f,
            level.Bounds.Bottom - 8f
        );
    }

    private static void AngryOshiro_OnPlayer(
        On.Celeste.AngryOshiro.orig_OnPlayer orig,
        AngryOshiro self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void AngryOshiro_OnPlayerBounce(
        On.Celeste.AngryOshiro.orig_OnPlayerBounce orig,
        AngryOshiro self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }
}
