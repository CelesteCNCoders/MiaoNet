using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Keeps the vanilla FinalBoss renderer while the watched client remains the
/// only authority for attacks, movement, collisions, camera and music.
/// </summary>
internal sealed class WatchFinalBossAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 36;
    private const float AnchorInterval = 0.1f;
    private const float HardReanchorDistance = 96f;

    private const byte HitEvent = 1;
    private const byte ChargeEvent = 2;

    private const byte VisibleFlag = 1 << 0;
    private const byte MovingFlag = 1 << 1;
    private const byte SittingFlag = 1 << 2;
    private const byte CollidableFlag = 1 << 3;
    private const byte BossSpriteFlag = 1 << 4;

    private static readonly HashSet<string> warnedUnknownAnimations = new(StringComparer.Ordinal);

    private readonly record struct BossState(
        byte Flags,
        WatchFinalBossAnimation Animation,
        byte AnimationFrame,
        int Facing,
        int NodeIndex,
        int PatternIndex,
        int Depth,
        Vector2 Position,
        Vector2 Scale,
        float LightAlpha
    );

    private readonly record struct SyncSignature(
        byte Flags,
        WatchFinalBossAnimation Animation,
        int Facing,
        int NodeIndex,
        int PatternIndex
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private SyncSignature signature;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, BossState current, bool force, float sceneTime)
        {
            SyncSignature currentSignature = new(
                current.Flags,
                current.Animation,
                current.Facing,
                current.NodeIndex,
                current.PatternIndex
            );
            bool movingAnchor = (current.Flags & MovingFlag) != 0 && sceneTime >= nextAnchorTime;
            if (force || !hasState || signature != currentSignature || movingAnchor)
            {
                state = Encode(id, current);
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
        public BossState State { get; set; }
        public Vector2 PositionStart { get; set; }
        public Vector2 PositionTarget { get; set; }
        public float Elapsed { get; set; }
        public float Duration { get; set; }

        public void Reset(BossState state)
        {
            PositionStart = PositionTarget = state.Position;
            Elapsed = Duration = 0f;
        }
    }

    private static readonly WatchFinalBossAdapter instance = new();
    private static readonly ConditionalWeakTable<FinalBoss, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<FinalBoss, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.FinalBoss;

    public static void Load()
    {
        On.Celeste.FinalBoss.ctor_EntityData_Vector2 += FinalBoss_ctor;
        On.Celeste.FinalBoss.Added += FinalBoss_Added;
        On.Celeste.FinalBoss.Awake += FinalBoss_Awake;
        On.Celeste.FinalBoss.Update += FinalBoss_Update;
        On.Celeste.FinalBoss.OnPlayer += FinalBoss_OnPlayer;
        On.Celeste.FinalBoss.PushPlayer += FinalBoss_PushPlayer;
        On.Celeste.FinalBoss.StartAttacking += FinalBoss_StartAttacking;
        On.Celeste.FinalBoss.StartShootCharge += FinalBoss_StartShootCharge;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FinalBoss.StartShootCharge -= FinalBoss_StartShootCharge;
        On.Celeste.FinalBoss.StartAttacking -= FinalBoss_StartAttacking;
        On.Celeste.FinalBoss.PushPlayer -= FinalBoss_PushPlayer;
        On.Celeste.FinalBoss.OnPlayer -= FinalBoss_OnPlayer;
        On.Celeste.FinalBoss.Update -= FinalBoss_Update;
        On.Celeste.FinalBoss.Awake -= FinalBoss_Awake;
        On.Celeste.FinalBoss.Added -= FinalBoss_Added;
        On.Celeste.FinalBoss.ctor_EntityData_Vector2 -= FinalBoss_ctor;
        WatchEntityIDTable<FinalBoss>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        warnedUnknownAnimations.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (FinalBoss boss in WatchRoomEntityIndex.Enumerate<FinalBoss>(level))
        {
            if (!WatchEntityIDTable<FinalBoss>.TryGet(boss, room, out int id))
                continue;
            yield return syncInfo.GetValue(boss, static _ => new()).Capture(
                id,
                Capture(boss),
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
        Dictionary<int, BossState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out BossState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        Dictionary<int, FinalBoss> existing = WatchRoomEntityIndex.Enumerate<FinalBoss>(level)
            .Select(entity => (
                Entity: entity,
                HasID: WatchEntityIDTable<FinalBoss>.TryGet(entity, room, out int id),
                ID: id
            ))
            .Where(item => item.HasID)
            .GroupBy(item => item.ID)
            .ToDictionary(group => group.Key, group => group.First().Entity);

        foreach ((int id, BossState state) in desired)
        {
            if (!existing.Remove(id, out FinalBoss? boss))
            {
                boss = Recreate(level, id);
                if (boss is null)
                    continue;
                changed = true;
            }
            changed |= ApplyAnchor(boss, state, level.TimeActive);
        }

        if (isCompleteState)
        {
            foreach (FinalBoss boss in existing.Values)
            {
                changed |= boss.Visible || boss.Collidable;
                DisableLocalBehavior(boss);
                boss.Visible = false;
                remoteInfo.GetValue(boss, static _ => new()).HasState = false;
            }
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        FinalBoss? boss = Find(level, entityEvent.Key.EntityID);
        if (boss is null || entityEvent.Payload.Length != 0)
            return;

        if (entityEvent.EventID == HitEvent)
        {
            EnsureBossSprite(boss);
            Sprite? sprite = ActiveSprite(boss);
            boss.scaleWiggler?.Start();
            TryPlayAnimation(sprite, "getHit");
        }
        else if (entityEvent.EventID == ChargeEvent)
        {
            EnsureBossSprite(boss);
            Sprite? sprite = ActiveSprite(boss);
            TryPlayAnimation(sprite, "attack1Begin");
        }
    }

    private static BossState Capture(FinalBoss boss)
    {
        byte flags = 0;
        if (boss.Visible) flags |= VisibleFlag;
        if (boss.Moving) flags |= MovingFlag;
        if (boss.Sitting) flags |= SittingFlag;
        if (boss.Collidable) flags |= CollidableFlag;
        if (boss.Sprite is not null) flags |= BossSpriteFlag;
        Sprite? sprite = ActiveSprite(boss);
        return new(
            flags,
            GetAnimation(sprite?.CurrentAnimationID),
            (byte)Math.Clamp(sprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            boss.facing < 0 ? -1 : 1,
            Math.Max(0, boss.nodeIndex),
            Math.Clamp(boss.patternIndex, 0, 32),
            boss.Depth,
            boss.Position,
            sprite?.Scale ?? Vector2.One,
            boss.light?.Alpha ?? 1f
        );
    }

    private static WatchEntityState Encode(int id, BossState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.FinalBoss, id), state, PayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                payload[1] = (byte)value.Animation;
                payload[2] = value.AnimationFrame;
                payload[3] = value.Facing < 0 ? (byte)1 : (byte)0;
                WatchEntityPayloadCodec.WriteInt32(payload, 4, value.NodeIndex);
                WatchEntityPayloadCodec.WriteInt32(payload, 8, value.PatternIndex);
                WatchEntityPayloadCodec.WriteInt32(payload, 12, value.Depth);
                WatchEntityPayloadCodec.WriteVector2(payload, 16, value.Position);
                WatchEntityPayloadCodec.WriteVector2(payload, 24, value.Scale);
                WatchEntityPayloadCodec.WriteSingle(payload, 32, value.LightAlpha);
            }
        );

    private static bool TryDecode(WatchEntityState state, out BossState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.FinalBoss || state.Key.SubID != 0
            || payload.Length != PayloadSize || (payload[0] & ~0b0001_1111) != 0
            || !IsValidAnimation(payload[1]) || payload[3] > 1)
            return false;
        Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 16);
        Vector2 scale = WatchEntityPayloadCodec.ReadVector2(payload, 24);
        float lightAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 32);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y)
            || !float.IsFinite(lightAlpha))
            return false;
        value = new(
            payload[0],
            (WatchFinalBossAnimation)payload[1],
            payload[2],
            payload[3] == 1 ? -1 : 1,
            WatchEntityPayloadCodec.ReadInt32(payload, 4),
            WatchEntityPayloadCodec.ReadInt32(payload, 8),
            WatchEntityPayloadCodec.ReadInt32(payload, 12),
            position,
            scale,
            lightAlpha
        );
        return true;
    }

    private static bool ApplyAnchor(FinalBoss boss, BossState state, float sceneTime)
    {
        RemoteInfo applied = remoteInfo.GetValue(boss, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || applied.State.NodeIndex != state.NodeIndex
            || applied.State.PatternIndex != state.PatternIndex
            || Vector2.DistanceSquared(boss.Position, state.Position)
                >= HardReanchorDistance * HardReanchorDistance;
        bool changed = !applied.HasState || applied.State != state;

        ApplyPresentation(boss, state);
        if (hard)
        {
            boss.Position = state.Position;
            applied.Reset(state);
        }
        else
        {
            applied.PositionStart = boss.Position;
            applied.PositionTarget = state.Position;
            applied.Elapsed = 0f;
            applied.Duration = AnchorInterval;
        }
        applied.State = state;
        applied.HasState = true;
        return changed;
    }

    private static void ApplyPresentation(FinalBoss boss, BossState state)
    {
        DisableLocalBehavior(boss);
        EnsurePresentationMode(boss, (state.Flags & BossSpriteFlag) != 0);
        boss.Visible = (state.Flags & VisibleFlag) != 0;
        boss.Moving = (state.Flags & MovingFlag) != 0;
        boss.Sitting = (state.Flags & SittingFlag) != 0;
        boss.facing = state.Facing;
        boss.nodeIndex = state.NodeIndex;
        boss.patternIndex = state.PatternIndex;
        boss.Depth = state.Depth;
        if (boss.light is not null)
            boss.light.Alpha = state.LightAlpha;
        Sprite? sprite = ActiveSprite(boss);
        if (sprite is not null)
        {
            if (TryGetAnimationName(state.Animation, out string animation))
            {
                bool animationAvailable = TryPlayAnimation(sprite, animation);
                if (animationAvailable && sprite.CurrentAnimationTotalFrames > 0)
                    sprite.SetAnimationFrame(Math.Min(state.AnimationFrame, sprite.CurrentAnimationTotalFrames - 1));
            }
            sprite.Scale = state.Scale;
        }
        if (boss.normalHair is not null)
            boss.normalHair.Facing = state.Facing < 0 ? Facings.Left : Facings.Right;
    }

    private static void DisableLocalBehavior(FinalBoss boss)
    {
        boss.Collidable = false;
        if (boss.attackCoroutine is not null)
            boss.attackCoroutine.Active = false;
        if (boss.triggerBlocksCoroutine is not null)
            boss.triggerBlocksCoroutine.Active = false;
    }

    private static FinalBoss? Recreate(Level level, int id)
    {
        LevelData levelData = level.Session.LevelData;
        EntityData? data = levelData.Entities.FirstOrDefault(candidate =>
            candidate.ID == id && candidate.Name == "finalBoss"
        );
        if (data is null)
            return null;
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        FinalBoss boss = new(data, offset);
        WatchEntityIDTable<FinalBoss>.Set(boss, level.Session.Level, id);
        level.Add(boss);
        return boss;
    }

    private static FinalBoss? Find(Level level, int id)
        => WatchEntityIDTable<FinalBoss>.Find(level, id);

    private static Sprite? ActiveSprite(FinalBoss boss)
        => boss.Sprite ?? boss.NormalSprite;

    private static void EnsurePresentationMode(FinalBoss boss, bool useBossSprite)
    {
        if (useBossSprite)
            EnsureBossSprite(boss);
        else
            EnsureNormalSprite(boss);
    }

    /// <summary>
    /// A vanilla projectile can only exist after FinalBoss.OnPlayer has switched
    /// pattern zero from its PlayerSprite presentation to badeline_boss. Rebuild
    /// that real presentation before vanilla Added reads ShotOrigin/BeamOrigin;
    /// never alias NormalSprite into Sprite because that suppresses the original
    /// one-way renderer transition.
    /// </summary>
    internal static bool EnsureBossSprite(FinalBoss boss)
    {
        if (boss.Sprite is not null && !ReferenceEquals(boss.Sprite, boss.NormalSprite))
            return true;

        // Recover safely if this instance survived a hot reload from the old
        // origin workaround, where both fields referenced the same component.
        if (ReferenceEquals(boss.Sprite, boss.NormalSprite))
            boss.Sprite = null;

        boss.CreateBossSprite();
        return boss.Sprite is not null;
    }

    private static void EnsureNormalSprite(FinalBoss boss)
    {
        if (ReferenceEquals(boss.Sprite, boss.NormalSprite))
            boss.Sprite = null;
        if (boss.NormalSprite is not null)
            return;

        if (boss.Sprite is not null)
        {
            boss.Remove(boss.Sprite);
            boss.Sprite = null;
        }

        boss.NormalSprite = new PlayerSprite(PlayerSpriteMode.Badeline);
        boss.NormalSprite.Scale.X = -1f;
        boss.NormalSprite.Play("laugh");
        boss.normalHair = new PlayerHair(boss.NormalSprite)
        {
            Color = BadelineOldsite.HairColor,
            Border = Color.Black,
            Facing = Facings.Left,
        };
        boss.Add(boss.normalHair);
        boss.Add(boss.NormalSprite);
    }

    private static bool TryPlayAnimation(Sprite? sprite, string animation)
    {
        if (sprite is null)
            return false;
        string? selected = WatchAnimationSelection.Select(
            animation,
            sprite.CurrentAnimationID,
            sprite.Has
        );
        if (selected is null)
            return false;
        if (sprite.CurrentAnimationID != selected)
            sprite.Play(selected, restart: true);
        return selected == animation;
    }

    private static WatchFinalBossAnimation GetAnimation(string? animation)
    {
        WatchFinalBossAnimation value = animation switch
        {
            "idle" => WatchFinalBossAnimation.Idle,
            "laugh" => WatchFinalBossAnimation.Laugh,
            "attack1Begin" => WatchFinalBossAnimation.Attack1Begin,
            "attack1Recoil" => WatchFinalBossAnimation.Attack1Recoil,
            "getHit" => WatchFinalBossAnimation.GetHit,
            "pretendDead" => WatchFinalBossAnimation.PretendDead,
            "attack1Loop" => WatchFinalBossAnimation.Attack1Loop,
            "attack2Begin" => WatchFinalBossAnimation.Attack2Begin,
            "attack2Aim" => WatchFinalBossAnimation.Attack2Aim,
            "attack2Lock" => WatchFinalBossAnimation.Attack2Lock,
            "attack2Recoil" => WatchFinalBossAnimation.Attack2Recoil,
            "star" => WatchFinalBossAnimation.Star,
            "recoverHit" => WatchFinalBossAnimation.RecoverHit,
            "scaredIdle" => WatchFinalBossAnimation.ScaredIdle,
            "scaredTransition" => WatchFinalBossAnimation.ScaredTransition,
            "calm" => WatchFinalBossAnimation.Calm,
            "lookUp" => WatchFinalBossAnimation.LookUp,
            "lookingUp" => WatchFinalBossAnimation.LookingUp,
            _ => WatchFinalBossAnimation.Unknown,
        };
        if (value == WatchFinalBossAnimation.Unknown
            && !string.IsNullOrEmpty(animation)
            && warnedUnknownAnimations.Add(animation))
            Logger.Warn(
                LT.MiaoNetWatch,
                $"FinalBoss produced unknown animation '{animation}'; preserving the Watcher animation."
            );
        return value;
    }

    private static bool TryGetAnimationName(
        WatchFinalBossAnimation animation,
        out string name
    )
    {
        name = animation switch
        {
            WatchFinalBossAnimation.Idle => "idle",
            WatchFinalBossAnimation.Laugh => "laugh",
            WatchFinalBossAnimation.Attack1Begin => "attack1Begin",
            WatchFinalBossAnimation.Attack1Recoil => "attack1Recoil",
            WatchFinalBossAnimation.GetHit => "getHit",
            WatchFinalBossAnimation.PretendDead => "pretendDead",
            WatchFinalBossAnimation.Attack1Loop => "attack1Loop",
            WatchFinalBossAnimation.Attack2Begin => "attack2Begin",
            WatchFinalBossAnimation.Attack2Aim => "attack2Aim",
            WatchFinalBossAnimation.Attack2Lock => "attack2Lock",
            WatchFinalBossAnimation.Attack2Recoil => "attack2Recoil",
            WatchFinalBossAnimation.Star => "star",
            WatchFinalBossAnimation.RecoverHit => "recoverHit",
            WatchFinalBossAnimation.ScaredIdle => "scaredIdle",
            WatchFinalBossAnimation.ScaredTransition => "scaredTransition",
            WatchFinalBossAnimation.Calm => "calm",
            WatchFinalBossAnimation.LookUp => "lookUp",
            WatchFinalBossAnimation.LookingUp => "lookingUp",
            _ => string.Empty,
        };
        return name.Length != 0;
    }

    private static bool IsValidAnimation(byte animation)
        => animation <= (byte)WatchFinalBossAnimation.LookingUp
            || animation == (byte)WatchFinalBossAnimation.Unknown;

    private static void FinalBoss_ctor(
        On.Celeste.FinalBoss.orig_ctor_EntityData_Vector2 orig,
        FinalBoss self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FinalBoss>.Set(self, data.Level.Name, data.ID);
    }

    private static void FinalBoss_Added(
        On.Celeste.FinalBoss.orig_Added orig,
        FinalBoss self,
        Scene scene
    )
    {
        if (!MiaoNetModule.IsWatching || scene is not Level level)
        {
            orig(self, scene);
            return;
        }

        bool hadIntro = level.Session.GetFlag("boss_intro");
        level.Session.SetFlag("boss_intro", true);
        try
        {
            orig(self, scene);
        }
        finally
        {
            level.Session.SetFlag("boss_intro", hadIntro);
        }
        DisableLocalBehavior(self);
    }

    private static void FinalBoss_Awake(
        On.Celeste.FinalBoss.orig_Awake orig,
        FinalBoss self,
        Scene scene
    )
    {
        orig(self, scene);
        if (MiaoNetModule.IsWatching)
        {
            DisableLocalBehavior(self);
        }
    }

    private static void FinalBoss_Update(On.Celeste.FinalBoss.orig_Update orig, FinalBoss self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        DisableLocalBehavior(self);
        self.Components.Update();
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
            return;
        if (applied.Duration > 0f)
        {
            applied.Elapsed = Math.Min(applied.Elapsed + Engine.DeltaTime, applied.Duration);
            self.Position = Vector2.Lerp(
                applied.PositionStart,
                applied.PositionTarget,
                applied.Elapsed / applied.Duration
            );
        }
    }

    private static void FinalBoss_OnPlayer(
        On.Celeste.FinalBoss.orig_OnPlayer orig,
        FinalBoss self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self, player);
        Publish(self, HitEvent);
    }

    private static void FinalBoss_PushPlayer(
        On.Celeste.FinalBoss.orig_PushPlayer orig,
        FinalBoss self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void FinalBoss_StartAttacking(
        On.Celeste.FinalBoss.orig_StartAttacking orig,
        FinalBoss self
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self);
    }

    private static void FinalBoss_StartShootCharge(
        On.Celeste.FinalBoss.orig_StartShootCharge orig,
        FinalBoss self
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self);
        Publish(self, ChargeEvent);
    }

    private static void Publish(FinalBoss boss, byte eventID)
    {
        if (boss.Scene is Level level
            && WatchEntityIDTable<FinalBoss>.TryGet(boss, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.FinalBoss, id), eventID, [])
            );
    }
}
