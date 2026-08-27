using MiaoNet.Shared;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal static class WatchHoldableEntityPayload
{
    private readonly record struct State(
        WatchHoldablePhase Phase,
        byte Flags,
        byte Animation,
        Vector2 Position,
        Vector2 Speed,
        float Rotation
    );

    public const int Size = 24;
    public const float CorrectionInterval = 0.1f;
    public const float ThrownStateDuration = 0.12f;
    public const byte PickupEvent = 1;
    public const byte ReleaseEvent = 2;
    public const byte DestroyEvent = 3;

    public static WatchEntityState Encode(
        WatchEntityKind kind,
        int id,
        WatchHoldablePhase phase,
        byte flags,
        byte animation,
        Vector2 position,
        Vector2 speed,
        float rotation
    )
        => WatchEntityState.FromTyped(
            new(kind, id),
            new State(phase, flags, animation, position, speed, rotation),
            Size,
            static (payload, state) =>
            {
                payload[0] = (byte)state.Phase;
                payload[1] = state.Flags;
                payload[2] = state.Animation;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, state.Position);
                WatchEntityPayloadCodec.WriteVector2(payload, 12, state.Speed);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, state.Rotation);
            }
        );

    public static bool TryParse(
        WatchEntityKind kind,
        WatchEntityState state,
        out WatchHoldablePhase phase,
        out byte flags,
        out byte animation,
        out Vector2 position,
        out Vector2 speed,
        out float rotation
    )
    {
        phase = default;
        flags = animation = default;
        position = speed = default;
        rotation = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != kind
            || state.Key.SubID != 0
            || payload.Length != Size
            || payload[0] > (byte)WatchHoldablePhase.Gone
            || (payload[1] & ~0b0000_1111) != 0
            || payload[2] > 8
            || payload[3] != 0)
            return false;

        phase = (WatchHoldablePhase)payload[0];
        flags = payload[1];
        animation = payload[2];
        position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
        speed = WatchEntityPayloadCodec.ReadVector2(payload, 12);
        rotation = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        return float.IsFinite(position.X) && float.IsFinite(position.Y)
            && float.IsFinite(speed.X) && float.IsFinite(speed.Y)
            && float.IsFinite(rotation);
    }

    public static byte[] EncodeRelease(Vector2 position, Vector2 force)
    {
        byte[] payload = new byte[16];
        WatchEntityPayloadCodec.WriteVector2(payload, 0, position);
        WatchEntityPayloadCodec.WriteVector2(payload, 8, force);
        return payload;
    }

    public static bool TryReadRelease(
        WatchEntityEvent entityEvent,
        out Vector2 position,
        out Vector2 force
    )
    {
        position = default;
        force = default;
        if (entityEvent.EventID != ReleaseEvent || entityEvent.Payload.Length != 16)
            return false;
        position = new(
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0),
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 4)
        );
        force = new(
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 8),
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 12)
        );
        return float.IsFinite(position.X) && float.IsFinite(position.Y)
            && float.IsFinite(force.X) && float.IsFinite(force.Y);
    }
}

internal sealed class WatchHoldableSyncInfo
{
    private const float IdlePositionToleranceSquared = 0.25f * 0.25f;

    private bool hasState;
    private WatchEntityState state;
    private WatchHoldablePhase statePhase;
    private byte stateFlags;
    private byte stateAnimation;
    private Vector2 statePosition;
    private float nextCorrectionAt;

    public string Level { get; private set; }
    public int ID { get; }
    public WatchHoldablePhase Phase { get; set; } = WatchHoldablePhase.Idle;
    public float ThrownUntil { get; set; }

    public WatchHoldableSyncInfo(string level, int id)
    {
        Level = level;
        ID = id;
    }

    public void SetLevel(string level)
    {
        if (Level == level)
            return;

        Level = level;
        hasState = false;
        nextCorrectionAt = 0f;
    }

    public WatchEntityState Capture(
        WatchEntityKind kind,
        WatchHoldablePhase phase,
        byte flags,
        byte animation,
        Vector2 position,
        Vector2 speed,
        float rotation,
        float timeActive
    )
    {
        if (phase == WatchHoldablePhase.Gone)
        {
            flags = 0;
            animation = 0;
            position = Vector2.Zero;
            speed = Vector2.Zero;
            rotation = 0f;
        }
        else if (phase is WatchHoldablePhase.Idle or WatchHoldablePhase.Carried)
        {
            speed = Vector2.Zero;
            rotation = 0f;
        }

        bool moving = phase is WatchHoldablePhase.Carried
            or WatchHoldablePhase.Thrown
            or WatchHoldablePhase.Moving
            or WatchHoldablePhase.Flying;
        bool idlePositionChanged = phase == WatchHoldablePhase.Idle
            && Vector2.DistanceSquared(position, statePosition) > IdlePositionToleranceSquared;
        bool refresh = !hasState
            || phase != statePhase
            || flags != stateFlags
            || animation != stateAnimation
            || idlePositionChanged
            || (moving && timeActive >= nextCorrectionAt);

        Phase = phase;
        if (refresh)
        {
            state = WatchHoldableEntityPayload.Encode(
                kind, ID, phase, flags, animation, position, speed, rotation
            );
            hasState = true;
            statePhase = phase;
            stateFlags = flags;
            stateAnimation = animation;
            statePosition = position;
            nextCorrectionAt = timeActive + WatchHoldableEntityPayload.CorrectionInterval;
        }
        return state;
    }
}

internal sealed class WatchTheoCrystalAdapter : IWatchEntityAdapter
{
    private sealed class SpectatorReplicaMarker
    {
    }

    private static readonly WatchTheoCrystalAdapter instance = new();
    private static readonly ConditionalWeakTable<TheoCrystal, WatchHoldableSyncInfo> infos = new();
    private static readonly ConditionalWeakTable<TheoCrystal, SpectatorReplicaMarker> spectatorReplicas = new();
    private static readonly Dictionary<(string Level, int ID), WatchHoldablePhase> phases = new();
    private static readonly HashSet<TheoCrystal> remoteDriven = new();

    public WatchEntityKind Kind => WatchEntityKind.TheoCrystal;

    public static void Load()
    {
        On.Celeste.TheoCrystal.ctor_EntityData_Vector2 += TheoCrystal_ctor;
        On.Celeste.TheoCrystal.OnPickup += TheoCrystal_OnPickup;
        On.Celeste.TheoCrystal.OnRelease += TheoCrystal_OnRelease;
        On.Celeste.TheoCrystal.Shatter += TheoCrystal_Shatter;
        On.Celeste.TheoCrystal.Die += TheoCrystal_Die;
        On.Celeste.HeartGem.OnHoldable += HeartGem_OnHoldable;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.HeartGem.OnHoldable -= HeartGem_OnHoldable;
        On.Celeste.TheoCrystal.Die -= TheoCrystal_Die;
        On.Celeste.TheoCrystal.Shatter -= TheoCrystal_Shatter;
        On.Celeste.TheoCrystal.OnRelease -= TheoCrystal_OnRelease;
        On.Celeste.TheoCrystal.OnPickup -= TheoCrystal_OnPickup;
        On.Celeste.TheoCrystal.ctor_EntityData_Vector2 -= TheoCrystal_ctor;
        remoteDriven.Clear();
        phases.Clear();
        spectatorReplicas.Clear();
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> live = new();
        foreach (TheoCrystal crystal in WatchRoomEntityIndex.Enumerate<TheoCrystal>(level))
        {
            if (!infos.TryGetValue(crystal, out WatchHoldableSyncInfo? info))
                continue;
            info.SetLevel(room);
            WatchHoldablePhase phase = crystal.dead
                ? WatchHoldablePhase.Gone
                : crystal.shattering
                    ? WatchHoldablePhase.Destroying
                    : crystal.Hold.IsHeld
                        ? WatchHoldablePhase.Carried
                        : level.TimeActive < info.ThrownUntil
                            ? WatchHoldablePhase.Thrown
                            : !crystal.OnGround() || crystal.Speed.LengthSquared() > 1f
                                ? WatchHoldablePhase.Moving
                                : WatchHoldablePhase.Idle;
            info.Phase = phases[(room, info.ID)] = phase;
            live.Add(info.ID);
            byte flags = 0;
            if (crystal.Visible) flags |= 1;
            if (crystal.Collidable) flags |= 2;
            if (crystal.OnPedestal) flags |= 4;
            if (crystal.dead) flags |= 8;
            yield return info.Capture(
                Kind, phase, flags, 0, crystal.Position, crystal.Speed,
                crystal.sprite.Rotation, level.TimeActive
            );
        }
        foreach (var pair in phases
            .Where(pair => pair.Key.Level == room && !live.Contains(pair.Key.ID))
            .OrderBy(pair => pair.Key.ID))
        {
            yield return WatchHoldableEntityPayload.Encode(
                Kind, pair.Key.ID, WatchHoldablePhase.Gone, 0, 0,
                Vector2.Zero, Vector2.Zero, 0f
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!WatchHoldableEntityPayload.TryParse(
                    Kind, state, out _, out _, out _, out _, out _, out _)
                || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        foreach (TheoCrystal crystal in WatchRoomEntityIndex.Enumerate<TheoCrystal>(level).ToArray())
        {
            if (!infos.TryGetValue(crystal, out WatchHoldableSyncInfo? info)
                || info.Level != level.Session.Level
                || !desired.Remove(info.ID, out WatchEntityState state)
                || !WatchHoldableEntityPayload.TryParse(
                    Kind, state, out WatchHoldablePhase phase, out byte flags,
                    out _, out Vector2 position, out Vector2 speed, out float rotation))
                continue;

            info.Phase = phases[(info.Level, info.ID)] = phase;
            spectatorReplicas.GetValue(crystal, static _ => new SpectatorReplicaMarker());
            crystal.Position = position;
            crystal.Speed = speed;
            crystal.sprite.Rotation = rotation;
            crystal.OnPedestal = (flags & 4) != 0;
            bool held = phase == WatchHoldablePhase.Carried;
            bool present = phase is not WatchHoldablePhase.Destroying and not WatchHoldablePhase.Gone;
            crystal.Active = !held && phase != WatchHoldablePhase.Gone;
            crystal.Visible = !held && present && (flags & 1) != 0;
            crystal.Collidable = !held && present && (flags & 2) != 0;
            if (phase == WatchHoldablePhase.Gone)
            {
                remoteDriven.Remove(crystal);
                crystal.RemoveSelf();
            }
            changed = true;
        }
        foreach ((int id, WatchEntityState state) in desired.ToArray())
        {
            if (!WatchHoldableEntityPayload.TryParse(
                    Kind, state, out WatchHoldablePhase phase, out byte flags,
                    out _, out Vector2 position, out Vector2 speed, out float rotation)
                || phase == WatchHoldablePhase.Gone)
                continue;
            TheoCrystal crystal = new(position);
            WatchHoldableSyncInfo info = new(level.Session.Level, id) { Phase = phase };
            infos.AddOrUpdate(crystal, info);
            spectatorReplicas.GetValue(crystal, static _ => new SpectatorReplicaMarker());
            phases[(info.Level, id)] = phase;
            crystal.Speed = speed;
            crystal.sprite.Rotation = rotation;
            crystal.OnPedestal = (flags & 4) != 0;
            bool held = phase == WatchHoldablePhase.Carried;
            crystal.Active = !held;
            crystal.Visible = !held && (flags & 1) != 0;
            crystal.Collidable = !held && (flags & 2) != 0;
            level.Add(crystal);
            desired.Remove(id);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        TheoCrystal? crystal = Find(level, entityEvent.Key.EntityID);
        if (crystal is null)
            return;
        switch (entityEvent.EventID)
        {
            case WatchHoldableEntityPayload.PickupEvent when entityEvent.Payload.Length == 0:
                crystal.Visible = false;
                crystal.Collidable = false;
                crystal.Speed = Vector2.Zero;
                break;
            case WatchHoldableEntityPayload.ReleaseEvent:
                if (!WatchHoldableEntityPayload.TryReadRelease(
                        entityEvent, out Vector2 position, out Vector2 force))
                    return;
                remoteDriven.Add(crystal);
                crystal.Position = position;
                crystal.Visible = true;
                crystal.OnRelease(force);
                remoteDriven.Remove(crystal);
                break;
            case WatchHoldableEntityPayload.DestroyEvent when entityEvent.Payload.Length == 0:
                remoteDriven.Add(crystal);
                crystal.Add(new Coroutine(RemoteShatter(crystal, crystal.Shatter())));
                break;
        }
    }

    private static IEnumerator RemoteShatter(TheoCrystal crystal, IEnumerator inner)
    {
        yield return inner;
        remoteDriven.Remove(crystal);
    }

    private static TheoCrystal? Find(Level level, int id)
        => WatchRoomEntityIndex.Enumerate<TheoCrystal>(level).FirstOrDefault(crystal =>
            infos.TryGetValue(crystal, out WatchHoldableSyncInfo? info)
            && info.Level == level.Session.Level && info.ID == id);

    private static void Publish(TheoCrystal self, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (self.Scene is Level level && infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
            WatchEntitySyncRegistry.PublishEvent(level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.TheoCrystal, info.ID), eventID, payload));
    }

    internal static bool IsSpectatorReplica(TheoCrystal crystal)
        => spectatorReplicas.TryGetValue(crystal, out _);

    private static void HeartGem_OnHoldable(
        On.Celeste.HeartGem.orig_OnHoldable orig,
        HeartGem self,
        Holdable holdable
    )
    {
        if (MiaoNetModule.IsWatching
            && holdable.Entity is TheoCrystal crystal
            && IsSpectatorReplica(crystal))
            return;

        orig(self, holdable);
    }

    private static void TheoCrystal_ctor(
        On.Celeste.TheoCrystal.orig_ctor_EntityData_Vector2 orig,
        TheoCrystal self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchHoldableSyncInfo info = new(data.Level.Name, data.ID);
        infos.AddOrUpdate(self, info);
        phases[(info.Level, info.ID)] = WatchHoldablePhase.Idle;
    }

    private static void TheoCrystal_OnPickup(On.Celeste.TheoCrystal.orig_OnPickup orig, TheoCrystal self)
    {
        orig(self);
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
        {
            info.ThrownUntil = 0f;
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Carried;
        }
        if (!remoteDriven.Contains(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, WatchHoldableEntityPayload.PickupEvent, []);
    }

    private static void TheoCrystal_OnRelease(
        On.Celeste.TheoCrystal.orig_OnRelease orig,
        TheoCrystal self,
        Vector2 force
    )
    {
        orig(self, force);
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
        {
            float timeActive = (self.Scene as Level)?.TimeActive ?? 0f;
            info.ThrownUntil = timeActive + WatchHoldableEntityPayload.ThrownStateDuration;
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Thrown;
        }
        if (!remoteDriven.Contains(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, WatchHoldableEntityPayload.ReleaseEvent,
                WatchHoldableEntityPayload.EncodeRelease(self.Position, force));
    }

    private static IEnumerator TheoCrystal_Shatter(
        On.Celeste.TheoCrystal.orig_Shatter orig,
        TheoCrystal self
    )
    {
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Destroying;
        if (!remoteDriven.Contains(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, WatchHoldableEntityPayload.DestroyEvent, []);
        return orig(self);
    }

    private static void TheoCrystal_Die(On.Celeste.TheoCrystal.orig_Die orig, TheoCrystal self)
    {
        if (IsSpectatorReplica(self))
        {
            self.Active = false;
            self.Visible = false;
            self.Collidable = false;
            return;
        }

        orig(self);
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Gone;
    }
}

internal sealed class WatchGliderAdapter : IWatchEntityAdapter
{
    private sealed class RemoteMotion
    {
        public bool HasTarget { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Speed { get; set; }
        public float Rotation { get; set; }
        public float Age { get; set; }
        public WatchHoldablePhase Phase { get; set; }
        public byte Animation { get; set; }
    }

    private static readonly WatchGliderAdapter instance = new();
    private static readonly ConditionalWeakTable<Glider, WatchHoldableSyncInfo> infos = new();
    private static readonly ConditionalWeakTable<Glider, RemoteMotion> remoteMotions = new();
    private static readonly Dictionary<(string Level, int ID), WatchHoldablePhase> phases = new();
    private static readonly HashSet<Glider> remoteDriven = new();

    public WatchEntityKind Kind => WatchEntityKind.Glider;

    public static void Load()
    {
        On.Celeste.Glider.ctor_EntityData_Vector2 += Glider_ctor;
        On.Celeste.Glider.Update += Glider_Update;
        On.Celeste.Glider.OnPickup += Glider_OnPickup;
        On.Celeste.Glider.OnRelease += Glider_OnRelease;
        On.Celeste.Glider.DestroyAnimationRoutine += Glider_DestroyAnimationRoutine;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Glider.DestroyAnimationRoutine -= Glider_DestroyAnimationRoutine;
        On.Celeste.Glider.OnRelease -= Glider_OnRelease;
        On.Celeste.Glider.OnPickup -= Glider_OnPickup;
        On.Celeste.Glider.Update -= Glider_Update;
        On.Celeste.Glider.ctor_EntityData_Vector2 -= Glider_ctor;
        remoteDriven.Clear();
        phases.Clear();
        remoteMotions.Clear();
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> live = new();
        foreach (Glider glider in WatchRoomEntityIndex.Enumerate<Glider>(level))
        {
            if (!infos.TryGetValue(glider, out WatchHoldableSyncInfo? info))
                continue;
            info.SetLevel(room);
            string animationID = glider.sprite.CurrentAnimationID;
            bool airborne = !glider.OnGround();
            WatchHoldablePhase phase = glider.destroyed
                ? WatchHoldablePhase.Destroying
                : glider.Hold.IsHeld
                    ? WatchHoldablePhase.Carried
                    : level.TimeActive < info.ThrownUntil
                        ? WatchHoldablePhase.Thrown
                        : airborne && animationID is "fall" or "fallLoop"
                            ? WatchHoldablePhase.Flying
                            : airborne || glider.Speed.LengthSquared() > 1f
                                ? WatchHoldablePhase.Moving
                                : WatchHoldablePhase.Idle;
            info.Phase = phases[(room, info.ID)] = phase;
            live.Add(info.ID);
            byte flags = 0;
            if (glider.Visible) flags |= 1;
            if (glider.Collidable) flags |= 2;
            if (glider.destroyed) flags |= 4;
            if (glider.bubble) flags |= 8;
            yield return info.Capture(
                Kind, phase, flags, EncodeAnimation(animationID), glider.Position,
                glider.Speed, glider.sprite.Rotation, level.TimeActive
            );
        }
        foreach (var pair in phases
            .Where(pair => pair.Key.Level == room && !live.Contains(pair.Key.ID))
            .OrderBy(pair => pair.Key.ID))
        {
            yield return WatchHoldableEntityPayload.Encode(
                Kind, pair.Key.ID, WatchHoldablePhase.Gone, 0, 0,
                Vector2.Zero, Vector2.Zero, 0f
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!WatchHoldableEntityPayload.TryParse(Kind, state, out _, out _, out _, out _, out _, out _)
                || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (Glider glider in WatchRoomEntityIndex.Enumerate<Glider>(level).ToArray())
        {
            if (!infos.TryGetValue(glider, out WatchHoldableSyncInfo? info) || info.Level != level.Session.Level
                || !desired.Remove(info.ID, out WatchEntityState state)
                || !WatchHoldableEntityPayload.TryParse(Kind, state, out WatchHoldablePhase phase,
                    out byte flags, out byte animation, out Vector2 position, out Vector2 speed, out float rotation))
                continue;
            bool enteredCarried = phase == WatchHoldablePhase.Carried
                && info.Phase != WatchHoldablePhase.Carried;
            if (enteredCarried)
            {
                remoteDriven.Add(glider);
                glider.OnPickup();
                remoteDriven.Remove(glider);
            }
            info.Phase = phases[(info.Level, info.ID)] = phase;
            ApplyRemoteTarget(glider, phase, animation, position, speed, rotation);
            glider.bubble = (flags & 8) != 0;
            bool held = phase == WatchHoldablePhase.Carried;
            bool present = phase is not WatchHoldablePhase.Destroying and not WatchHoldablePhase.Gone;
            glider.Active = !held && phase != WatchHoldablePhase.Gone;
            glider.Visible = !held && present && (flags & 1) != 0;
            glider.Collidable = !held && present && (flags & 2) != 0;
            string? animationID = ResolveAnimation(phase, animation, speed);
            if (animationID is not null && glider.sprite.CurrentAnimationID != animationID)
                glider.sprite.Play(animationID);
            if (phase == WatchHoldablePhase.Gone)
            {
                remoteDriven.Remove(glider);
                glider.RemoveSelf();
            }
            changed = true;
        }
        foreach ((int id, WatchEntityState state) in desired.ToArray())
        {
            if (!WatchHoldableEntityPayload.TryParse(
                    Kind, state, out WatchHoldablePhase phase, out byte flags,
                    out byte animation, out Vector2 position, out Vector2 speed, out float rotation)
                || phase == WatchHoldablePhase.Gone)
                continue;
            Glider glider = new(position, (flags & 8) != 0, false);
            WatchHoldableSyncInfo info = new(level.Session.Level, id) { Phase = phase };
            infos.AddOrUpdate(glider, info);
            phases[(info.Level, id)] = phase;
            // A carried spectator replica is reconstructed from authoritative state;
            // it did not undergo a local pickup.
            ApplyRemoteTarget(glider, phase, animation, position, speed, rotation);
            bool held = phase == WatchHoldablePhase.Carried;
            glider.Active = !held;
            glider.Visible = !held && (flags & 1) != 0;
            glider.Collidable = !held && (flags & 2) != 0;
            string? animationID = ResolveAnimation(phase, animation, speed);
            if (animationID is not null)
                glider.sprite.Play(animationID);
            level.Add(glider);
            desired.Remove(id);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        Glider? glider = Find(level, entityEvent.Key.EntityID);
        if (glider is null)
            return;
        switch (entityEvent.EventID)
        {
            case WatchHoldableEntityPayload.PickupEvent when entityEvent.Payload.Length == 0:
                if (remoteMotions.TryGetValue(glider, out RemoteMotion? pickupMotion))
                    pickupMotion.Phase = WatchHoldablePhase.Carried;
                glider.Active = false;
                glider.Visible = false;
                glider.Collidable = false;
                glider.Speed = Vector2.Zero;
                break;
            case WatchHoldableEntityPayload.ReleaseEvent:
                if (!WatchHoldableEntityPayload.TryReadRelease(
                        entityEvent, out Vector2 position, out Vector2 force))
                    return;
                remoteDriven.Add(glider);
                glider.Position = position;
                glider.Active = true;
                glider.Visible = true;
                glider.Collidable = true;
                glider.bubble = false;
                glider.OnRelease(force);
                ApplyRemoteTarget(
                    glider,
                    WatchHoldablePhase.Thrown,
                    EncodeAnimation(glider.sprite.CurrentAnimationID),
                    position,
                    glider.Speed,
                    glider.sprite.Rotation
                );
                remoteDriven.Remove(glider);
                break;
            case WatchHoldableEntityPayload.DestroyEvent when entityEvent.Payload.Length == 0:
                remoteDriven.Add(glider);
                glider.Add(new Coroutine(RemoteDestroy(glider, glider.DestroyAnimationRoutine())));
                break;
        }
    }

    private static IEnumerator RemoteDestroy(Glider glider, IEnumerator inner)
    {
        yield return inner;
        remoteDriven.Remove(glider);
    }

    private static Glider? Find(Level level, int id)
        => WatchRoomEntityIndex.Enumerate<Glider>(level).FirstOrDefault(glider =>
            infos.TryGetValue(glider, out WatchHoldableSyncInfo? info)
            && info.Level == level.Session.Level && info.ID == id);

    private static void Publish(Glider self, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (self.Scene is Level level && infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
            WatchEntitySyncRegistry.PublishEvent(level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Glider, info.ID), eventID, payload));
    }

    private static byte EncodeAnimation(string animationID) => animationID switch
    {
        "idle" => 1,
        "held" => 2,
        "fall" => 3,
        "fallLoop" => 4,
        "death" => 5,
        _ => 0,
    };

    private static string? DecodeAnimation(byte animation) => animation switch
    {
        1 => "idle",
        2 => "held",
        3 => "fall",
        4 => "fallLoop",
        5 => "death",
        _ => null,
    };

    private static string? ResolveAnimation(
        WatchHoldablePhase phase,
        byte animation,
        Vector2 speed
    )
    {
        string? animationID = DecodeAnimation(animation);
        if (phase == WatchHoldablePhase.Carried)
            return "held";

        // Glider.OnRelease changes velocity but does not change the Sprite until
        // the following vanilla Update. A state captured in that gap still says
        // "held"; do not let that stale carried animation overwrite the ordered
        // release event on the watcher.
        if (animationID == "held")
        {
            if (phase is WatchHoldablePhase.Thrown or WatchHoldablePhase.Flying)
                return speed.Y < 0f ? "fall" : "fallLoop";
            return "idle";
        }

        if (phase == WatchHoldablePhase.Flying
            && animationID is not ("fall" or "fallLoop"))
            return speed.Y < 0f ? "fall" : "fallLoop";
        return animationID;
    }

    private static void ApplyRemoteTarget(
        Glider glider,
        WatchHoldablePhase phase,
        byte animation,
        Vector2 position,
        Vector2 speed,
        float rotation
    )
    {
        RemoteMotion motion = remoteMotions.GetValue(glider, static _ => new RemoteMotion());
        bool initialize = !motion.HasTarget;
        motion.HasTarget = true;
        motion.Position = position;
        motion.Speed = speed;
        motion.Rotation = rotation;
        motion.Age = 0f;
        motion.Phase = phase;
        motion.Animation = animation;
        glider.Speed = speed;
        if (initialize)
        {
            glider.Position = position;
            glider.sprite.Rotation = rotation;
        }
    }

    private static void Glider_Update(On.Celeste.Glider.orig_Update orig, Glider self)
    {
        if (!MiaoNetModule.IsWatching
            || !remoteMotions.TryGetValue(self, out RemoteMotion? motion)
            || !motion.HasTarget
            || motion.Phase is WatchHoldablePhase.Carried
                or WatchHoldablePhase.Destroying
                or WatchHoldablePhase.Gone)
        {
            orig(self);
            return;
        }

        Vector2 displayedPosition = self.Position;
        float displayedRotation = self.sprite.Rotation;
        orig(self);
        if (self.Scene is null)
            return;

        float deltaTime = Engine.RawDeltaTime;
        motion.Age = Math.Min(
            motion.Age + deltaTime,
            WatchHoldableEntityPayload.CorrectionInterval * 1.5f
        );
        Vector2 predictedPosition = motion.Position + motion.Speed * motion.Age;
        float blend = 1f - MathF.Exp(-20f * deltaTime);
        self.Position = Vector2.Lerp(displayedPosition, predictedPosition, blend);
        self.Speed = motion.Speed;
        self.sprite.Rotation = MathHelper.Lerp(displayedRotation, motion.Rotation, blend);

        string? animationID = ResolveAnimation(motion.Phase, motion.Animation, motion.Speed);
        if (animationID is not null && self.sprite.CurrentAnimationID != animationID)
            self.sprite.Play(animationID);
    }

    private static void Glider_ctor(On.Celeste.Glider.orig_ctor_EntityData_Vector2 orig, Glider self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        WatchHoldableSyncInfo info = new(data.Level.Name, data.ID);
        infos.AddOrUpdate(self, info);
        phases[(info.Level, info.ID)] = WatchHoldablePhase.Idle;
    }

    private static void Glider_OnPickup(On.Celeste.Glider.orig_OnPickup orig, Glider self)
    {
        orig(self);
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
        {
            info.ThrownUntil = 0f;
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Carried;
        }
        if (!remoteDriven.Contains(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, WatchHoldableEntityPayload.PickupEvent, []);
    }

    private static void Glider_OnRelease(On.Celeste.Glider.orig_OnRelease orig, Glider self, Vector2 force)
    {
        orig(self, force);
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
        {
            float timeActive = (self.Scene as Level)?.TimeActive ?? 0f;
            info.ThrownUntil = timeActive + WatchHoldableEntityPayload.ThrownStateDuration;
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Thrown;
        }
        if (!remoteDriven.Contains(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, WatchHoldableEntityPayload.ReleaseEvent,
                WatchHoldableEntityPayload.EncodeRelease(self.Position, force));
    }

    private static IEnumerator Glider_DestroyAnimationRoutine(On.Celeste.Glider.orig_DestroyAnimationRoutine orig, Glider self)
    {
        if (infos.TryGetValue(self, out WatchHoldableSyncInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Destroying;
        if (!remoteDriven.Contains(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, WatchHoldableEntityPayload.DestroyEvent, []);
        return TrackDestroy(self, orig(self));
    }

    private static IEnumerator TrackDestroy(Glider glider, IEnumerator inner)
    {
        yield return inner;
        if (infos.TryGetValue(glider, out WatchHoldableSyncInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchHoldablePhase.Gone;
    }
}

internal sealed class WatchTheoCrystalPedestalAdapter : IWatchEntityAdapter
{
    private static readonly WatchTheoCrystalPedestalAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.TheoCrystalPedestal;

    public static void Load()
    {
        On.Celeste.TheoCrystalPedestal.ctor += Pedestal_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.TheoCrystalPedestal.ctor -= Pedestal_ctor;
        WatchEntityIDTable<TheoCrystalPedestal>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (TheoCrystalPedestal pedestal in WatchRoomEntityIndex.Enumerate<TheoCrystalPedestal>(level))
        {
            if (WatchEntityIDTable<TheoCrystalPedestal>.TryGet(pedestal, level.Session.Level, out int id))
                yield return WatchEntityState.FromTyped(
                    new(Kind, id),
                    pedestal.DroppedTheo,
                    static value => [value ? (byte)1 : (byte)0]
                );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, bool> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind || state.Key.SubID != 0 || state.Payload.Length != 1
                || state.Payload.Span[0] > 1 || !desired.TryAdd(state.Key.EntityID, state.Payload.Span[0] != 0))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (TheoCrystalPedestal pedestal in WatchRoomEntityIndex.Enumerate<TheoCrystalPedestal>(level))
        {
            if (!WatchEntityIDTable<TheoCrystalPedestal>.TryGet(pedestal, level.Session.Level, out int id)
                || !desired.TryGetValue(id, out bool dropped) || pedestal.DroppedTheo == dropped)
                continue;
            pedestal.DroppedTheo = dropped;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void Pedestal_ctor(
        On.Celeste.TheoCrystalPedestal.orig_ctor orig,
        TheoCrystalPedestal self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<TheoCrystalPedestal>.Set(self, data.Level.Name, data.ID);
    }
}
