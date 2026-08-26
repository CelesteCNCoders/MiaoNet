using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchSnowballAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte ResetEvent = 1;
    private const byte BreakEvent = 2;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const float AnchorInterval = 0.1f;
    private const float CorrectionFactor = 0.35f;
    private const float HardPositionError = 16f;

    private readonly record struct SnowballState(
        WatchSnowballPhase Phase,
        byte Flags,
        byte AnimationFrame,
        Vector2 Position,
        float AtY,
        float ResetTimer,
        float SineCounter
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private WatchSnowballPhase phase;
        private byte flags;
        private float nextAnchorTime;
        private WatchEntityState state;

        public bool Broken { get; set; }

        public WatchEntityState Capture(int id, SnowballState current, bool forceCurrent, float sceneTime)
        {
            if (forceCurrent || !hasState || phase != current.Phase || flags != current.Flags
                || sceneTime >= nextAnchorTime)
            {
                state = Encode(id, current);
                phase = current.Phase;
                flags = current.Flags;
                hasState = true;
                nextAnchorTime = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public SnowballState State { get; set; }
    }

    private static readonly WatchSnowballAdapter instance = new();
    private static readonly ConditionalWeakTable<Snowball, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<Snowball, RemoteInfo> remoteInfo = new();
    private static (string Room, int ID)? spawnContext;
    private static byte breakReason;

    public WatchEntityKind Kind => WatchEntityKind.Snowball;

    public static void Load()
    {
        On.Celeste.WindAttackTrigger.ctor += WindAttackTrigger_ctor;
        On.Celeste.WindAttackTrigger.OnEnter += WindAttackTrigger_OnEnter;
        On.Celeste.Snowball.ctor += Snowball_ctor;
        On.Celeste.Snowball.ResetPosition += Snowball_ResetPosition;
        On.Celeste.Snowball.Destroy += Snowball_Destroy;
        On.Celeste.Snowball.OnPlayer += Snowball_OnPlayer;
        On.Celeste.Snowball.OnPlayerBounce += Snowball_OnPlayerBounce;
        On.Celeste.Snowball.Update += Snowball_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Snowball.Update -= Snowball_Update;
        On.Celeste.Snowball.OnPlayerBounce -= Snowball_OnPlayerBounce;
        On.Celeste.Snowball.OnPlayer -= Snowball_OnPlayer;
        On.Celeste.Snowball.Destroy -= Snowball_Destroy;
        On.Celeste.Snowball.ResetPosition -= Snowball_ResetPosition;
        On.Celeste.Snowball.ctor -= Snowball_ctor;
        On.Celeste.WindAttackTrigger.OnEnter -= WindAttackTrigger_OnEnter;
        On.Celeste.WindAttackTrigger.ctor -= WindAttackTrigger_ctor;
        WatchEntityIDTable<WindAttackTrigger>.Clear();
        WatchEntityIDTable<Snowball>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        spawnContext = null;
        breakReason = 0;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Snowball snowball in WatchRoomEntityIndex.Enumerate<Snowball>(level))
        {
            if (!WatchEntityIDTable<Snowball>.TryGet(snowball, room, out int id))
                continue;
            SyncInfo info = syncInfo.GetValue(snowball, static _ => new());
            SnowballState current = Capture(snowball, info.Broken);
            yield return info.Capture(
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
        Dictionary<int, SnowballState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out SnowballState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (Snowball snowball in WatchRoomEntityIndex.Enumerate<Snowball>(level))
        {
            if (!WatchEntityIDTable<Snowball>.TryGet(snowball, room, out int id))
                continue;
            RemoteInfo applied = remoteInfo.GetValue(snowball, static _ => new());
            if (desired.Remove(id, out SnowballState value))
            {
                bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset || !applied.HasState;
                changed |= hard ? Apply(snowball, value) : ApplyCorrection(snowball, applied.State, value);
                changed |= hard;
                applied.State = value;
                applied.HasState = true;
            }
            else if (isCompleteState)
            {
                changed |= snowball.Visible || snowball.Collidable || applied.HasState;
                snowball.Visible = false;
                snowball.Collidable = false;
                applied.HasState = false;
            }
        }

        foreach ((int id, SnowballState value) in desired)
        {
            Snowball snowball = new();
            WatchEntityIDTable<Snowball>.Set(snowball, room, id);
            level.Add(snowball);
            Apply(snowball, value);
            RemoteInfo applied = remoteInfo.GetValue(snowball, static _ => new());
            applied.State = value;
            applied.HasState = true;
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        Snowball? snowball = Find(level, entityEvent.Key.EntityID);
        if (snowball is null)
            return;
        if (entityEvent.EventID == ResetEvent && entityEvent.Payload.Length == 0)
        {
            Audio.Play("event:/game/04_cliffside/snowball_spawn", snowball.Position);
            snowball.sprite.Play("spin", restart: true);
        }
        else if (entityEvent.EventID == BreakEvent && entityEvent.Payload.Length == 1)
        {
            snowball.sprite.Play("break", restart: true);
            Audio.Play(entityEvent.Payload.Span[0] == 1
                ? "event:/game/general/thing_booped"
                : "event:/game/04_cliffside/snowball_impact", snowball.Position);
        }
    }

    private static SnowballState Capture(Snowball snowball, bool broken)
    {
        byte flags = 0;
        if (snowball.Visible) flags |= VisibleFlag;
        if (snowball.Collidable) flags |= CollidableFlag;
        return new(
            broken ? WatchSnowballPhase.Broken : WatchSnowballPhase.Active,
            flags,
            (byte)Math.Clamp(snowball.sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            snowball.Position,
            snowball.atY,
            snowball.resetTimer,
            snowball.sine.Counter
        );
    }

    private static WatchEntityState Encode(int id, SnowballState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.Snowball, id), state, PayloadSize,
            static (payload, value) =>
            {
                payload[0] = (byte)value.Phase;
                payload[1] = value.Flags;
                payload[2] = value.Phase == WatchSnowballPhase.Broken ? (byte)1 : (byte)0;
                payload[3] = value.AnimationFrame;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteSingle(payload, 12, value.AtY);
                WatchEntityPayloadCodec.WriteSingle(payload, 16, value.ResetTimer);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, value.SineCounter);
            }
        );

    private static bool TryDecode(WatchEntityState state, out SnowballState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.Snowball || state.Key.SubID != 0
            || payload.Length != PayloadSize || payload[0] > (byte)WatchSnowballPhase.Broken
            || (payload[1] & ~0b0000_0011) != 0 || payload[2] > 1
            || payload[2] != payload[0])
            return false;
        Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
        float atY = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float resetTimer = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float sineCounter = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(atY)
            || !float.IsFinite(resetTimer) || !float.IsFinite(sineCounter))
            return false;
        value = new(
            (WatchSnowballPhase)payload[0],
            payload[1],
            payload[3],
            position,
            atY,
            resetTimer,
            sineCounter
        );
        return true;
    }

    private static bool Apply(Snowball snowball, SnowballState state)
    {
        bool visible = (state.Flags & VisibleFlag) != 0;
        bool changed = snowball.Position != state.Position || snowball.Visible != visible
            || snowball.Collidable || snowball.atY != state.AtY
            || Capture(snowball, state.Phase == WatchSnowballPhase.Broken) != state;
        snowball.Position = state.Position;
        snowball.Visible = visible;
        snowball.Collidable = false;
        snowball.atY = state.AtY;
        snowball.resetTimer = state.ResetTimer;
        snowball.sine.Counter = state.SineCounter;
        ApplyAnimation(snowball, state, synchronizeFrame: true);
        return changed;
    }

    private static bool ApplyCorrection(Snowball snowball, SnowballState previous, SnowballState state)
    {
        if (previous.Phase != state.Phase || previous.Flags != state.Flags
            || Vector2.Distance(snowball.Position, state.Position) >= HardPositionError)
            return Apply(snowball, state);
        Vector2 position = Vector2.Lerp(snowball.Position, state.Position, CorrectionFactor);
        bool changed = snowball.Position != position || snowball.atY != state.AtY;
        snowball.Position = position;
        snowball.atY = MathHelper.Lerp(snowball.atY, state.AtY, CorrectionFactor);
        snowball.resetTimer = state.ResetTimer;
        MaintainAnimation(snowball, state);
        return changed;
    }

    private static void ApplyAnimation(
        Snowball snowball,
        SnowballState state,
        bool synchronizeFrame
    )
    {
        string animation = state.Phase == WatchSnowballPhase.Broken ? "break" : "spin";
        if (snowball.sprite.CurrentAnimationID != animation)
            snowball.sprite.Play(animation, restart: true);
        if (synchronizeFrame && snowball.sprite.CurrentAnimationTotalFrames > 0)
            snowball.sprite.SetAnimationFrame(Math.Min(state.AnimationFrame, snowball.sprite.CurrentAnimationTotalFrames - 1));
    }

    private static void MaintainAnimation(Snowball snowball, SnowballState state)
    {
        if (state.Phase == WatchSnowballPhase.Active)
        {
            if (snowball.sprite.CurrentAnimationID != "spin")
                snowball.sprite.Play("spin", restart: true);
        }
        else if (snowball.sprite.CurrentAnimationID == "spin")
        {
            // A completed one-shot break animation has an empty animation ID.
            // Leave that terminal frame alone instead of restarting break or
            // treating the entity as active again.
            snowball.sprite.Play("break", restart: true);
        }
    }

    private static Snowball? Find(Level level, int id)
    {
        string room = level.Session.Level;
        return WatchEntityIDTable<Snowball>.Find(level, room, id);
    }

    private static void WindAttackTrigger_ctor(
        On.Celeste.WindAttackTrigger.orig_ctor orig,
        WindAttackTrigger self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<WindAttackTrigger>.Set(self, data.Level.Name, data.ID);
    }

    private static void WindAttackTrigger_OnEnter(
        On.Celeste.WindAttackTrigger.orig_OnEnter orig,
        WindAttackTrigger self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        (string Room, int ID)? previous = spawnContext;
        if (self.Scene is Level level
            && WatchEntityIDTable<WindAttackTrigger>.TryGet(self, level.Session.Level, out int id))
            spawnContext = (level.Session.Level, id);
        try
        {
            orig(self, player);
        }
        finally
        {
            spawnContext = previous;
        }
    }

    private static void Snowball_ctor(On.Celeste.Snowball.orig_ctor orig, Snowball self)
    {
        orig(self);
        if (spawnContext is { } parent)
            WatchEntityIDTable<Snowball>.Set(self, parent.Room, parent.ID);
    }

    private static void Snowball_ResetPosition(
        On.Celeste.Snowball.orig_ResetPosition orig,
        Snowball self
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        Vector2 previousPosition = self.Position;
        bool previousVisible = self.Visible;
        orig(self);
        bool resetSucceeded = self.Visible && self.Collidable && self.resetTimer <= 0f;
        if (resetSucceeded)
            syncInfo.GetValue(self, static _ => new()).Broken = false;
        if (resetSucceeded && (!previousVisible || self.Position != previousPosition)
            && self.Scene is Level level
            && WatchEntityIDTable<Snowball>.TryGet(self, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Snowball, id), ResetEvent, [])
            );
    }

    private static void Snowball_Destroy(On.Celeste.Snowball.orig_Destroy orig, Snowball self)
    {
        if (MiaoNetModule.IsWatching)
            return;
        bool wasCollidable = self.Collidable;
        orig(self);
        if (wasCollidable)
            syncInfo.GetValue(self, static _ => new()).Broken = true;
        if (wasCollidable && self.Scene is Level level
            && WatchEntityIDTable<Snowball>.TryGet(self, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Snowball, id), BreakEvent, [breakReason])
            );
    }

    private static void Snowball_OnPlayer(
        On.Celeste.Snowball.orig_OnPlayer orig,
        Snowball self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        byte previous = breakReason;
        breakReason = 0;
        try { orig(self, player); }
        finally { breakReason = previous; }
    }

    private static void Snowball_OnPlayerBounce(
        On.Celeste.Snowball.orig_OnPlayerBounce orig,
        Snowball self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;
        byte previous = breakReason;
        breakReason = 1;
        try { orig(self, player); }
        finally { breakReason = previous; }
    }

    private static void Snowball_Update(On.Celeste.Snowball.orig_Update orig, Snowball self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.sprite.Update();
        self.sine.Update();
        if (remoteInfo.TryGetValue(self, out RemoteInfo? applied) && applied.HasState)
        {
            self.X -= 200f * Engine.DeltaTime;
            self.Y = self.atY + self.sine.Value * 4f;
        }
    }
}
