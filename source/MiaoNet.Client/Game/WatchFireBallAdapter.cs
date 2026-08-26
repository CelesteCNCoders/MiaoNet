using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchFireBallAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte BreakEvent = 1;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte IceModeFlag = 1 << 2;
    private const byte BrokenFlag = 1 << 3;
    private const byte NotCoreModeFlag = 1 << 4;
    private const float AnchorInterval = 0.1f;
    private const float CorrectionFactor = 0.35f;
    private const float HardPositionError = 12f;
    private const float HardPhaseError = 0.2f;

    private readonly record struct FireBallState(
        byte Flags,
        Vector2 Position,
        float Percent,
        float Speed,
        float SpeedMultiplier
    );

    private sealed class FireBallSyncInfo
    {
        private bool hasState;
        private byte flags;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(
            int id,
            ushort subID,
            FireBallState current,
            bool forceCurrent,
            float sceneTime
        )
        {
            if (forceCurrent
                || !hasState
                || flags != current.Flags
                || sceneTime >= nextAnchorTime)
            {
                state = Encode(id, subID, current);
                flags = current.Flags;
                hasState = true;
                nextAnchorTime = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteApplyInfo
    {
        public bool HasState { get; set; }
        public FireBallState State { get; set; }
    }

    private static readonly WatchFireBallAdapter instance = new();
    private static readonly ConditionalWeakTable<FireBall, FireBallSyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<FireBall, RemoteApplyInfo> remoteApplyInfo = new();
    private static (string Room, int ID)? spawnContext;

    public WatchEntityKind Kind => WatchEntityKind.FireBall;

    public static void Load()
    {
        On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float_bool += FireBall_ctor_Vector;
        On.Celeste.FireBall.ctor_EntityData_Vector2 += FireBall_ctor_EntityData;
        On.Celeste.FireBall.Added += FireBall_Added;
        On.Celeste.FireBall.Update += FireBall_Update;
        On.Celeste.FireBall.OnPlayer += FireBall_OnPlayer;
        On.Celeste.FireBall.OnBounce += FireBall_OnBounce;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FireBall.OnBounce -= FireBall_OnBounce;
        On.Celeste.FireBall.OnPlayer -= FireBall_OnPlayer;
        On.Celeste.FireBall.Update -= FireBall_Update;
        On.Celeste.FireBall.Added -= FireBall_Added;
        On.Celeste.FireBall.ctor_EntityData_Vector2 -= FireBall_ctor_EntityData;
        On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float_bool -= FireBall_ctor_Vector;
        WatchEntityIDTable<FireBall>.Clear();
        syncInfo.Clear();
        remoteApplyInfo.Clear();
        spawnContext = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (FireBall fireBall in level.Entities.OfType<FireBall>())
        {
            if (fireBall.index is < 0 or > ushort.MaxValue
                || !WatchEntityIDTable<FireBall>.TryGet(fireBall, room, out int id))
                continue;
            ushort subID = (ushort)fireBall.index;
            FireBallState current = Capture(fireBall);
            yield return syncInfo.GetValue(fireBall, static _ => new())
                .Capture(
                    id,
                    subID,
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
        Dictionary<(int ID, ushort SubID), FireBallState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out FireBallState value)
                || !desired.TryAdd((state.Key.EntityID, state.Key.SubID), value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        bool requiresReload = false;
        string room = level.Session.Level;
        foreach (FireBall fireBall in level.Entities.OfType<FireBall>())
        {
            if (fireBall.index is < 0 or > ushort.MaxValue
                || !WatchEntityIDTable<FireBall>.TryGet(fireBall, room, out int id))
                continue;
            var key = (id, (ushort)fireBall.index);
            if (desired.Remove(key, out FireBallState value))
            {
                RemoteApplyInfo applied = remoteApplyInfo.GetValue(fireBall, static _ => new());
                if (WatchEntitySyncRegistry.IsApplyingLifecycleReset || !applied.HasState)
                {
                    changed |= Apply(fireBall, value);
                    applied.State = value;
                    applied.HasState = true;
                }
                else if (applied.State != value)
                {
                    changed |= ApplyCorrection(fireBall, value);
                    applied.State = value;
                }
            }
            else if (isCompleteState)
                requiresReload = true;
        }
        if (desired.Count > 0)
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
        if (entityEvent.Key.Kind != Kind
            || entityEvent.EventID != BreakEvent
            || entityEvent.Payload.Length != 0)
            return;

        FireBall? fireBall = Find(level, entityEvent.Key.EntityID, entityEvent.Key.SubID);
        if (fireBall is null)
            return;
        PlayBreak(fireBall, level);
    }

    private static FireBallState Capture(FireBall fireBall)
    {
        byte flags = 0;
        if (fireBall.Visible)
            flags |= VisibleFlag;
        if (fireBall.Collidable)
            flags |= CollidableFlag;
        if (fireBall.iceMode)
            flags |= IceModeFlag;
        if (fireBall.broken)
            flags |= BrokenFlag;
        if (fireBall.notCoreMode)
            flags |= NotCoreModeFlag;
        return new(flags, fireBall.Position, fireBall.percent, fireBall.speed, fireBall.speedMult);
    }

    private static WatchEntityState Encode(int id, ushort subID, FireBallState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.Percent);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.Speed);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.SpeedMultiplier);
        return new(new WatchEntityKey(WatchEntityKind.FireBall, id, subID), payload);
    }

    private static bool TryDecode(WatchEntityState state, out FireBallState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.FireBall
            || payload.Length != PayloadSize
            || (payload[0] & ~(VisibleFlag | CollidableFlag | IceModeFlag | BrokenFlag | NotCoreModeFlag)) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        Vector2 position = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 4),
            WatchEntityPayloadCodec.ReadSingle(payload, 8)
        );
        float percent = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float speed = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float speedMultiplier = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(percent) || !float.IsFinite(speed) || !float.IsFinite(speedMultiplier))
            return false;
        value = new(payload[0], position, percent, speed, speedMultiplier);
        return true;
    }

    private static bool Apply(FireBall fireBall, FireBallState desired)
    {
        bool visible = (desired.Flags & VisibleFlag) != 0;
        bool collidable = (desired.Flags & CollidableFlag) != 0;
        bool iceMode = (desired.Flags & IceModeFlag) != 0;
        bool broken = (desired.Flags & BrokenFlag) != 0;
        bool notCoreMode = (desired.Flags & NotCoreModeFlag) != 0;
        bool modeChanged = fireBall.iceMode != iceMode || fireBall.broken != broken;
        bool changed = fireBall.Position != desired.Position
            || fireBall.Visible != visible
            || fireBall.Collidable != collidable
            || modeChanged
            || fireBall.notCoreMode != notCoreMode
            || fireBall.percent != desired.Percent
            || fireBall.speed != desired.Speed
            || fireBall.speedMult != desired.SpeedMultiplier;

        fireBall.Position = desired.Position;
        fireBall.Visible = visible;
        fireBall.Collidable = collidable;
        fireBall.iceMode = iceMode;
        fireBall.broken = broken;
        fireBall.notCoreMode = notCoreMode;
        fireBall.percent = desired.Percent;
        fireBall.speed = desired.Speed;
        fireBall.speedMult = desired.SpeedMultiplier;
        if (modeChanged)
            fireBall.sprite.Play(broken ? "shatter" : (iceMode ? "ice" : "hot"), restart: true);
        return changed;
    }

    private static bool ApplyCorrection(FireBall fireBall, FireBallState desired)
    {
        float phaseDelta = WrapUnitDelta(desired.Percent - fireBall.percent);
        if (Capture(fireBall).Flags != desired.Flags
            || MathF.Sign(fireBall.speed) != MathF.Sign(desired.Speed)
            || Vector2.Distance(fireBall.Position, desired.Position) >= HardPositionError
            || Math.Abs(phaseDelta) >= HardPhaseError)
            return Apply(fireBall, desired);

        Vector2 position = Vector2.Lerp(
            fireBall.Position,
            desired.Position,
            CorrectionFactor
        );
        float percent = WrapUnit(fireBall.percent + phaseDelta * CorrectionFactor);
        float speed = MathHelper.Lerp(fireBall.speed, desired.Speed, CorrectionFactor);
        float speedMultiplier = MathHelper.Lerp(
            fireBall.speedMult,
            desired.SpeedMultiplier,
            CorrectionFactor
        );
        bool changed = fireBall.Position != position
            || fireBall.percent != percent
            || fireBall.speed != speed
            || fireBall.speedMult != speedMultiplier;
        fireBall.Position = position;
        fireBall.percent = percent;
        fireBall.speed = speed;
        fireBall.speedMult = speedMultiplier;
        return changed;
    }

    private static float WrapUnitDelta(float value)
        => value - MathF.Floor(value + 0.5f);

    private static float WrapUnit(float value)
        => value - MathF.Floor(value);

    private static FireBall? Find(Level level, int id, ushort subID)
    {
        string room = level.Session.Level;
        return level.Entities.OfType<FireBall>().FirstOrDefault(candidate =>
            candidate.index == subID
            && WatchEntityIDTable<FireBall>.TryGet(candidate, room, out int candidateID)
            && candidateID == id
        );
    }

    private static void PlayBreak(FireBall fireBall, Level level)
    {
        Audio.Play("event:/game/09_core/iceball_break", fireBall.Position);
        fireBall.sprite.Play("shatter");
        fireBall.broken = true;
        fireBall.Collidable = false;
        level.Particles.Emit(FireBall.P_IceBreak, 18, fireBall.Center, Vector2.One * 6f);
    }

    private static void FireBall_ctor_Vector(
        On.Celeste.FireBall.orig_ctor_Vector2Array_int_int_float_float_bool orig,
        FireBall self,
        Vector2[] nodes,
        int amount,
        int index,
        float offset,
        float speedMultiplier,
        bool notCoreMode
    )
    {
        orig(self, nodes, amount, index, offset, speedMultiplier, notCoreMode);
        if (spawnContext is { } parent)
            WatchEntityIDTable<FireBall>.Set(self, parent.Room, parent.ID);
    }

    private static void FireBall_ctor_EntityData(
        On.Celeste.FireBall.orig_ctor_EntityData_Vector2 orig,
        FireBall self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FireBall>.Set(self, data.Level.Name, data.ID);
    }

    private static void FireBall_Added(
        On.Celeste.FireBall.orig_Added orig,
        FireBall self,
        Scene scene
    )
    {
        (string Room, int ID)? previous = spawnContext;
        if (self.index == 0
            && scene is Level level
            && WatchEntityIDTable<FireBall>.TryGet(self, level.Session.Level, out int id))
            spawnContext = (level.Session.Level, id);
        try
        {
            orig(self, scene);
        }
        finally
        {
            spawnContext = previous;
        }
    }

    private static void FireBall_OnPlayer(
        On.Celeste.FireBall.orig_OnPlayer orig,
        FireBall self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void FireBall_Update(
        On.Celeste.FireBall.orig_Update orig,
        FireBall self
    )
    {
        if (!MiaoNetModule.IsWatching || !MiaoNetModule.IsWatchedPlayerPaused)
            orig(self);
    }

    private static void FireBall_OnBounce(
        On.Celeste.FireBall.orig_OnBounce orig,
        FireBall self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;

        bool wasBroken = self.broken;
        orig(self, player);
        if (wasBroken || !self.broken || self.Scene is not Level level)
            return;
        string room = level.Session.Level;
        if (self.index is < 0 or > ushort.MaxValue
            || !WatchEntityIDTable<FireBall>.TryGet(self, room, out int id))
            return;
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(
                new WatchEntityKey(WatchEntityKind.FireBall, id, (ushort)self.index),
                BreakEvent,
                []
            )
        );
    }
}
