using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchLightningBreakerBoxAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const float AnchorInterval = 0.1f;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte HitEvent = 1;
    private const byte BreakEvent = 2;

    private readonly record struct BoxState(
        byte Flags,
        byte Health,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 Scale,
        float Rotation
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private float nextAnchor;
        private BoxState last;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, BoxState current, float sceneTime, bool force)
        {
            bool signatureChanged = !hasState
                || last.Flags != current.Flags
                || last.Health != current.Health
                || last.Animation != current.Animation;
            bool visualChanged = !hasState
                || last.AnimationFrame != current.AnimationFrame
                || last.Position != current.Position
                || last.Scale != current.Scale
                || last.Rotation != current.Rotation;
            if (force || signatureChanged || (visualChanged && sceneTime >= nextAnchor))
            {
                state = Encode(id, current);
                last = current;
                hasState = true;
                nextAnchor = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private static readonly WatchLightningBreakerBoxAdapter instance = new();
    private static readonly ConditionalWeakTable<LightningBreakerBox, SyncInfo> syncInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.LightningBreakerBox;

    public static void Load()
    {
        On.Celeste.LightningBreakerBox.ctor_EntityData_Vector2 += LightningBreakerBox_ctor;
        On.Celeste.LightningBreakerBox.Dashed += LightningBreakerBox_Dashed;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.LightningBreakerBox.Dashed -= LightningBreakerBox_Dashed;
        On.Celeste.LightningBreakerBox.ctor_EntityData_Vector2 -= LightningBreakerBox_ctor;
        WatchEntityIDTable<LightningBreakerBox>.Clear();
        syncInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (LightningBreakerBox box in WatchRoomEntityIndex.Enumerate<LightningBreakerBox>(level))
        {
            if (!WatchEntityIDTable<LightningBreakerBox>.TryGet(box, level.Session.Level, out int id))
                continue;
            yield return syncInfo.GetValue(box, static _ => new()).Capture(
                id,
                Capture(box),
                level.TimeActive,
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
        Dictionary<int, BoxState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out BoxState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        Dictionary<int, LightningBreakerBox> existing = WatchRoomEntityIndex.Enumerate<LightningBreakerBox>(level)
            .Select(box => (
                Box: box,
                HasID: WatchEntityIDTable<LightningBreakerBox>.TryGet(box, room, out int id),
                ID: id
            ))
            .Where(item => item.HasID)
            .GroupBy(item => item.ID)
            .ToDictionary(group => group.Key, group => group.First().Box);

        foreach ((int id, BoxState value) in desired)
        {
            if (!existing.Remove(id, out LightningBreakerBox? box))
            {
                box = Recreate(level, id);
                if (box is null)
                    continue;
            }
            Apply(box, value);
            changed = true;
        }

        if (isCompleteState)
        {
            foreach (LightningBreakerBox box in existing.Values)
            {
                box.Visible = false;
                box.Collidable = false;
                changed = true;
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.Payload.Length != 8)
            return;
        LightningBreakerBox? box = Find(level, entityEvent.Key.EntityID);
        if (box is null)
            return;
        Vector2 direction = new(
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0),
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 4)
        );
        if (entityEvent.EventID == HitEvent)
        {
            Audio.Play("event:/new_content/game/10_farewell/fusebox_hit_1", box.Position);
            level.DirectionalShake(direction);
            box.shakeCounter = 0.2f;
            if (box.shaker is not null)
                box.shaker.On = true;
            box.bounceDir = direction;
            box.bounce?.Start();
            WatchLightningAdapter.CaptureRendererBaseline(level);
            box.Pulse();
            Vector2 debrisDirection = Calc.Perpendicular(direction);
            box.SmashParticles(debrisDirection);
            box.SmashParticles(-debrisDirection);
        }
        else if (entityEvent.EventID == BreakEvent)
        {
            Audio.Play("event:/new_content/game/10_farewell/fusebox_hit_2", box.Position);
            level.DirectionalShake(direction);
            box.Tag = Tags.Persistent;
            box.shakeCounter = 0f;
            if (box.shaker is not null)
                box.shaker.On = false;
            if (box.pulseRoutine is not null)
                box.pulseRoutine.Active = false;
            box.firstHitSfx?.Stop(true);
            box.Collidable = false;
            box.DisableStaticMovers();
            if (box.sprite?.Has("break") == true)
                box.sprite.Play("break", restart: true);
            Vector2 debrisDirection = Calc.Perpendicular(direction);
            box.SmashParticles(debrisDirection);
            box.SmashParticles(-debrisDirection);
        }
    }

    private static BoxState Capture(LightningBreakerBox box)
    {
        byte flags = 0;
        if (box.Visible) flags |= VisibleFlag;
        if (box.Collidable) flags |= CollidableFlag;
        string? animation = box.sprite?.CurrentAnimationID;
        byte animationID = animation switch
        {
            "idle" => 0,
            "open" => 1,
            "opened" => 2,
            "break" => 3,
            _ => byte.MaxValue,
        };
        return new(
            flags,
            (byte)Math.Clamp(box.health, 0, 2),
            animationID,
            (byte)Math.Clamp(box.sprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            box.Position,
            box.sprite?.Scale ?? Vector2.One,
            box.sprite?.Rotation ?? 0f
        );
    }

    private static WatchEntityState Encode(int id, BoxState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.LightningBreakerBox, id), state, PayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                payload[1] = value.Health;
                payload[2] = value.Animation;
                payload[3] = value.AnimationFrame;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteVector2(payload, 12, value.Scale);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, value.Rotation);
            }
        );

    private static bool TryDecode(WatchEntityState state, out BoxState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.LightningBreakerBox
            || state.Key.SubID != 0
            || payload.Length != PayloadSize
            || (payload[0] & ~0b0000_0011) != 0
            || payload[1] > 2
            || (payload[2] > 3 && payload[2] != byte.MaxValue))
            return false;
        float x = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        float y = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        float scaleX = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float scaleY = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float rotation = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(scaleX)
            || !float.IsFinite(scaleY) || !float.IsFinite(rotation))
            return false;
        value = new(payload[0], payload[1], payload[2], payload[3],
            new(x, y), new(scaleX, scaleY), rotation);
        return true;
    }

    private static void Apply(LightningBreakerBox box, BoxState state)
    {
        box.health = state.Health;
        box.Visible = (state.Flags & VisibleFlag) != 0;
        box.Collidable = false;
        if (state.Health > 0 && box.Visible)
            box.EnableStaticMovers();
        else
        {
            if (box.pulseRoutine is not null)
                box.pulseRoutine.Active = false;
            box.DisableStaticMovers();
        }
        // Move only the box and its attachments; the hidden Watcher Player must
        // never become a rider. Assigning Position alone leaves vanilla spikes
        // behind when the fuse box sinks/bounces.
        Vector2 movement = state.Position - box.Position;
        box.Position = state.Position;
        if (movement != Vector2.Zero)
        {
            box.MoveStaticMovers(movement);
            box.ClearRemainder();
        }
        if (box.sprite is null)
            return;
        string? animation = state.Animation switch
        {
            0 => "idle",
            1 => "open",
            2 => "opened",
            3 => "break",
            _ => null,
        };
        if (animation is not null && box.sprite.Has(animation))
        {
            bool animationChanged = box.sprite.CurrentAnimationID != animation;
            if (animationChanged)
                box.sprite.Play(animation, restart: true);
            if ((animationChanged || WatchEntitySyncRegistry.IsApplyingLifecycleReset)
                && box.sprite.CurrentAnimationTotalFrames > 0)
                box.sprite.SetAnimationFrame(Math.Min(
                    state.AnimationFrame,
                    box.sprite.CurrentAnimationTotalFrames - 1
                ));
        }
        box.sprite.Scale = state.Scale;
        box.sprite.Rotation = state.Rotation;
    }

    private static LightningBreakerBox? Find(Level level, int id)
        => WatchEntityIDTable<LightningBreakerBox>.Find(level, id);

    private static LightningBreakerBox? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "lightningBlock"
        );
        if (data is null)
            return null;
        LightningBreakerBox box = new(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<LightningBreakerBox>.Set(box, level.Session.Level, id);
        level.Add(box);
        return box;
    }

    private static void LightningBreakerBox_ctor(
        On.Celeste.LightningBreakerBox.orig_ctor_EntityData_Vector2 orig,
        LightningBreakerBox self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<LightningBreakerBox>.Set(self, data.Level.Name, data.ID);
    }

    private static DashCollisionResults LightningBreakerBox_Dashed(
        On.Celeste.LightningBreakerBox.orig_Dashed orig,
        LightningBreakerBox self,
        Player player,
        Vector2 direction
    )
    {
        if (MiaoNetModule.IsWatching)
            return DashCollisionResults.NormalCollision;
        int health = self.health;
        DashCollisionResults result = orig(self, player, direction);
        if (self.health != health && self.Scene is Level level
            && WatchEntityIDTable<LightningBreakerBox>.TryGet(
                self,
                level.Session.Level,
                out int id
            ))
        {
            byte[] payload = new byte[8];
            WatchEntityPayloadCodec.WriteVector2(payload, 0, direction);
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.LightningBreakerBox, id),
                self.health <= 0 ? BreakEvent : HitEvent,
                payload
            ));
        }
        return result;
    }
}

internal sealed class WatchLightningAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const float AnchorInterval = 0.1f;
    private const float ForceEdgeRefreshDistance = 64f;
    private const byte VisibleFlag = 1 << 0;
    private const byte DisappearingFlag = 1 << 1;
    private const byte MovingFlag = 1 << 2;
    private const byte TowardEndFlag = 1 << 3;
    private const byte ShatterEvent = 1;

    private readonly record struct LightningState(
        byte Flags,
        Vector2 Position,
        float Fade,
        float RendererFade,
        float MotionPhase
    );

    private sealed class Metadata
    {
        public bool Moving { get; init; }
        public Vector2 Start { get; init; }
        public Vector2 End { get; init; }
        public float MoveTime { get; init; }
        public bool TowardEnd { get; set; } = true;
    }

    private sealed class SyncInfo
    {
        private bool hasState;
        private byte flags;
        private float fade;
        private float rendererFade;
        private float nextAnchor;
        private WatchEntityState state;

        public WatchEntityState Capture(
            int id,
            LightningState current,
            float sceneTime,
            bool force
        )
        {
            bool continuous = (current.Flags & (MovingFlag | DisappearingFlag)) != 0;
            if (force || !hasState || flags != current.Flags
                || (!continuous && (fade != current.Fade || rendererFade != current.RendererFade))
                || (continuous && sceneTime >= nextAnchor))
            {
                state = Encode(id, current);
                flags = current.Flags;
                fade = current.Fade;
                rendererFade = current.RendererFade;
                hasState = true;
                nextAnchor = sceneTime + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public Vector2 Start { get; set; }
        public Vector2 Target { get; set; }
        public float Elapsed { get; set; }
        public float MotionPhase { get; set; }
        public bool TowardEnd { get; set; }
    }

    private sealed class RendererRemoteInfo
    {
        public bool HasBaseline { get; set; }
        public float BloomStrength { get; set; }
        public float GlitchValue { get; set; }
        public bool Breaking { get; set; }
        public bool Removed { get; set; }
        public float FadeStart { get; set; }
        public float FadeTarget { get; set; }
        public float FadeElapsed { get; set; }
    }

    private sealed class EdgeCameraInfo
    {
        public bool HasPosition { get; set; }
        public string Room { get; set; } = string.Empty;
        public Vector2 Position { get; set; }
    }

    private static readonly WatchLightningAdapter instance = new();
    private static readonly ConditionalWeakTable<Lightning, Metadata> metadata = new();
    private static readonly ConditionalWeakTable<Lightning, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<Lightning, RemoteInfo> remoteInfo = new();
    private static readonly ConditionalWeakTable<Level, RendererRemoteInfo> rendererRemoteInfo = new();
    private static readonly ConditionalWeakTable<Level, EdgeCameraInfo> edgeCameraInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.Lightning;

    public static void Load()
    {
        On.Celeste.Lightning.ctor_EntityData_Vector2 += Lightning_ctor;
        On.Celeste.Lightning.Update += Lightning_Update;
        On.Celeste.Lightning.OnPlayer += Lightning_OnPlayer;
        On.Celeste.Lightning.Shatter += Lightning_Shatter;
        On.Celeste.LightningRenderer.Track += LightningRenderer_Track;
        On.Celeste.LightningRenderer.Untrack += LightningRenderer_Untrack;
        On.Celeste.LightningRenderer.RebuildEdges += LightningRenderer_RebuildEdges;
        On.Celeste.LightningRenderer.Update += LightningRenderer_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.LightningRenderer.Update -= LightningRenderer_Update;
        On.Celeste.LightningRenderer.RebuildEdges -= LightningRenderer_RebuildEdges;
        On.Celeste.LightningRenderer.Untrack -= LightningRenderer_Untrack;
        On.Celeste.LightningRenderer.Track -= LightningRenderer_Track;
        On.Celeste.Lightning.Shatter -= Lightning_Shatter;
        On.Celeste.Lightning.OnPlayer -= Lightning_OnPlayer;
        On.Celeste.Lightning.Update -= Lightning_Update;
        On.Celeste.Lightning.ctor_EntityData_Vector2 -= Lightning_ctor;
        WatchEntityIDTable<Lightning>.Clear();
        metadata.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        rendererRemoteInfo.Clear();
        edgeCameraInfo.Clear();
    }

    internal static void RefreshRendererEdgesForCamera(Level level)
    {
        LightningRenderer? renderer = level.Tracker.GetEntity<LightningRenderer>();
        if (renderer is null)
            return;

        EdgeCameraInfo info = edgeCameraInfo.GetValue(level, static _ => new());
        Vector2 cameraPosition = level.Camera.Position;
        bool force = !info.HasPosition
            || info.Room != level.Session.Level
            || Vector2.DistanceSquared(info.Position, cameraPosition)
                >= ForceEdgeRefreshDistance * ForceEdgeRefreshDistance;

        // LightningRenderer.Update runs before the authoritative Watcher camera
        // is restored at the end of Level.Update. Re-run its culling pass against
        // the final camera so stale visibility flags cannot leave broken outlines.
        renderer.ToggleEdges(force);
        info.HasPosition = true;
        info.Room = level.Session.Level;
        info.Position = cameraPosition;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        float rendererFade = level.Tracker.GetEntity<LightningRenderer>()?.Fade ?? 0f;
        foreach (Lightning lightning in WatchRoomEntityIndex.Enumerate<Lightning>(level))
        {
            if (!WatchEntityIDTable<Lightning>.TryGet(lightning, level.Session.Level, out int id))
                continue;
            bool moving = metadata.TryGetValue(lightning, out Metadata? info) && info.Moving;
            byte flags = 0;
            if (lightning.Visible) flags |= VisibleFlag;
            if (lightning.disappearing) flags |= DisappearingFlag;
            if (moving) flags |= MovingFlag;
            if (moving && info!.TowardEnd) flags |= TowardEndFlag;
            yield return syncInfo.GetValue(lightning, static _ => new()).Capture(
                id,
                new(
                    flags,
                    lightning.Position,
                    lightning.Fade,
                    rendererFade,
                    moving ? CalculateMotionPhase(info!, lightning.Position) : 0f
                ),
                level.TimeActive,
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
        Dictionary<int, LightningState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out LightningState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        bool removedAny = false;
        string room = level.Session.Level;
        bool disappearing = desired.Values.Any(state =>
            (state.Flags & DisappearingFlag) != 0
        );
        float rendererFade = desired.Count == 0
            ? 0f
            : desired.Values.Max(state => state.RendererFade);
        foreach (Lightning lightning in WatchRoomEntityIndex.Enumerate<Lightning>(level))
        {
            if (!WatchEntityIDTable<Lightning>.TryGet(lightning, room, out int id))
                continue;
            if (!desired.Remove(id, out LightningState value))
            {
                if (isCompleteState)
                {
                    RemoveRemoteLightning(lightning);
                    removedAny = true;
                    changed = true;
                }
                continue;
            }
            Apply(lightning, value);
            changed = true;
        }

        foreach ((int id, LightningState value) in desired)
        {
            Lightning? lightning = Recreate(level, id);
            if (lightning is null)
                continue;
            Apply(lightning, value);
            changed = true;
        }
        if (desired.Count > 0 || states.Count > 0)
            ApplyRendererState(level, disappearing, rendererFade);
        else if (isCompleteState && removedAny)
            FinishRendererRemoval(level);
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ShatterEvent || entityEvent.Payload.Length != 0)
            return;
        Lightning? lightning = Find(level, entityEvent.Key.EntityID);
        if (lightning is null)
            return;
        lightning.Shatter();
    }

    private static WatchEntityState Encode(int id, LightningState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.Lightning, id), state, PayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteSingle(payload, 12, value.Fade);
                WatchEntityPayloadCodec.WriteSingle(payload, 16, value.RendererFade);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, value.MotionPhase);
            }
        );

    private static bool TryDecode(WatchEntityState state, out LightningState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.Lightning || state.Key.SubID != 0
            || payload.Length != PayloadSize || (payload[0] & ~0b0000_1111) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        float x = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        float y = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        float fade = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float rendererFade = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float motionPhase = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(fade)
            || !float.IsFinite(rendererFade) || !float.IsFinite(motionPhase)
            || fade is < 0f or > 1f || rendererFade is < 0f or > 0.6f)
            return false;
        if (motionPhase is < 0f or > 1f
            || (state.Payload.Span[0] & MovingFlag) == 0
                && ((state.Payload.Span[0] & TowardEndFlag) != 0 || motionPhase != 0f))
            return false;
        value = new(payload[0], new(x, y), fade, rendererFade, motionPhase);
        return true;
    }

    private static void Apply(Lightning lightning, LightningState state)
    {
        RemoteInfo applied = remoteInfo.GetValue(lightning, static _ => new());
        bool moving = (state.Flags & MovingFlag) != 0;
        bool towardEnd = (state.Flags & TowardEndFlag) != 0;
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || Vector2.DistanceSquared(lightning.Position, state.Position) >= 48f * 48f;
        if (moving && metadata.TryGetValue(lightning, out Metadata? motion))
        {
            bool directionChanged = applied.HasState && applied.TowardEnd != towardEnd;
            float phaseError = Math.Abs(applied.MotionPhase - state.MotionPhase);
            if (hard || directionChanged || phaseError >= 0.15f)
                applied.MotionPhase = state.MotionPhase;
            else
                applied.MotionPhase = MathHelper.Lerp(
                    applied.MotionPhase,
                    state.MotionPhase,
                    0.35f
                );
            applied.TowardEnd = towardEnd;
            lightning.Position = PositionAtPhase(motion, applied.MotionPhase);
            applied.Start = applied.Target = lightning.Position;
            applied.Elapsed = AnchorInterval;
        }
        else if (hard || !moving)
        {
            lightning.Position = state.Position;
            applied.Start = applied.Target = state.Position;
            applied.Elapsed = AnchorInterval;
        }
        else
        {
            applied.Start = lightning.Position;
            applied.Target = state.Position;
            applied.Elapsed = 0f;
        }
        applied.HasState = true;
        lightning.Fade = state.Fade;
        lightning.disappearing = (state.Flags & DisappearingFlag) != 0;
        lightning.Visible = (state.Flags & VisibleFlag) != 0;
        lightning.Collidable = false;
    }

    private static float CalculateMotionPhase(Metadata metadata, Vector2 position)
    {
        Vector2 axis = metadata.End - metadata.Start;
        float lengthSquared = axis.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return 0f;
        float eased = Calc.Clamp(
            Vector2.Dot(position - metadata.Start, axis) / lengthSquared,
            0f,
            1f
        );
        return MathF.Acos(1f - 2f * eased) / MathF.PI;
    }

    private static Vector2 PositionAtPhase(Metadata metadata, float phase)
        => Vector2.Lerp(
            metadata.Start,
            metadata.End,
            Ease.SineInOut(Calc.Clamp(phase, 0f, 1f))
        );

    private static void RemoveRemoteLightning(Lightning lightning)
    {
        remoteInfo.GetValue(lightning, static _ => new()).HasState = false;
        lightning.Visible = false;
        lightning.Collidable = false;
        lightning.RemoveSelf();
    }

    internal static void CaptureRendererBaseline(Level level)
    {
        RendererRemoteInfo info = rendererRemoteInfo.GetValue(level, static _ => new());
        if (info.HasBaseline)
            return;
        info.HasBaseline = true;
        info.BloomStrength = level.Bloom.Strength;
        info.GlitchValue = Glitch.Value;
    }

    private static void ApplyRendererState(Level level, bool disappearing, float fade)
    {
        LightningRenderer? renderer = level.Tracker.GetEntity<LightningRenderer>();
        if (renderer is null)
            return;

        RendererRemoteInfo info = rendererRemoteInfo.GetValue(level, static _ => new());
        if (disappearing)
        {
            if (!info.Breaking)
            {
                CaptureRendererBaseline(level);
                renderer.StopAmbience();
                renderer.UpdateSeeds = false;
            }
            info.Breaking = true;
            info.Removed = false;
            if (WatchEntitySyncRegistry.IsApplyingLifecycleReset)
            {
                info.FadeStart = info.FadeTarget = fade;
                info.FadeElapsed = AnchorInterval;
                ApplyRendererVisual(level, renderer, fade);
            }
            else
            {
                info.FadeStart = renderer.Fade;
                info.FadeTarget = fade;
                info.FadeElapsed = 0f;
            }
            return;
        }

        renderer.Fade = fade;
        if (!info.Breaking && !info.Removed)
            return;
        RestoreRendererBaseline(level, info);
        renderer.UpdateSeeds = true;
        renderer.StartAmbience();
        info.Breaking = false;
        info.Removed = false;
    }

    private static void FinishRendererRemoval(Level level)
    {
        LightningRenderer? renderer = level.Tracker.GetEntity<LightningRenderer>();
        if (renderer is null)
            return;
        RendererRemoteInfo info = rendererRemoteInfo.GetValue(level, static _ => new());
        RestoreRendererBaseline(level, info);
        renderer.Fade = 0f;
        renderer.UpdateSeeds = false;
        renderer.StopAmbience();
        info.Breaking = false;
        info.Removed = true;
        info.FadeStart = info.FadeTarget = 0f;
        info.FadeElapsed = AnchorInterval;
    }

    private static void RestoreRendererBaseline(Level level, RendererRemoteInfo info)
    {
        if (!info.HasBaseline)
            return;
        level.Bloom.Strength = info.BloomStrength;
        Glitch.Value = info.GlitchValue;
    }

    private static void ApplyRendererVisual(
        Level level,
        LightningRenderer renderer,
        float fade
    )
    {
        float amount = Calc.Clamp(fade / 0.6f, 0f, 1f);
        renderer.Fade = fade;
        level.Bloom.Strength = MathHelper.Lerp(1f, 1.5f, amount);
        Glitch.Value = MathHelper.Lerp(0f, 0.15f, amount);
    }

    private static Lightning? Find(Level level, int id)
        => WatchEntityIDTable<Lightning>.Find(level, id);

    private static Lightning? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "lightning"
        );
        if (data is null)
            return null;
        Lightning lightning = new(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<Lightning>.Set(lightning, level.Session.Level, id);
        metadata.AddOrUpdate(lightning, CreateMetadata(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        )));
        level.Add(lightning);
        return lightning;
    }

    private static void Lightning_ctor(
        On.Celeste.Lightning.orig_ctor_EntityData_Vector2 orig,
        Lightning self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Lightning>.Set(self, data.Level.Name, data.ID);
        metadata.AddOrUpdate(self, CreateMetadata(data, offset));
    }

    private static Metadata CreateMetadata(EntityData data, Vector2 offset)
    {
        Vector2 start = data.Position + offset;
        Vector2 end = data.Nodes.Length > 0 ? data.Nodes[0] + offset : start;
        return new()
        {
            Moving = data.Nodes.Length > 0,
            Start = start,
            End = end,
            MoveTime = data.Float("moveTime", 0f),
        };
    }

    private static void LightningRenderer_Track(
        On.Celeste.LightningRenderer.orig_Track orig,
        LightningRenderer self,
        Lightning lightning
    )
    {
        RunAtTopologyPosition(lightning, () => orig(self, lightning));
    }

    private static void LightningRenderer_Untrack(
        On.Celeste.LightningRenderer.orig_Untrack orig,
        LightningRenderer self,
        Lightning lightning
    )
    {
        RunAtTopologyPosition(lightning, () => orig(self, lightning));
    }

    private static void LightningRenderer_RebuildEdges(
        On.Celeste.LightningRenderer.orig_RebuildEdges orig,
        LightningRenderer self
    )
    {
        if (!MiaoNetModule.IsWatching || self.Scene is not Level level)
        {
            orig(self);
            return;
        }

        List<(Lightning Entity, Vector2 Position)> moved = new();
        foreach (Lightning lightning in WatchRoomEntityIndex.Enumerate<Lightning>(level))
        {
            if (!metadata.TryGetValue(lightning, out Metadata? info)
                || !info.Moving || lightning.Position == info.Start)
                continue;
            moved.Add((lightning, lightning.Position));
            lightning.Position = info.Start;
        }

        try
        {
            orig(self);
        }
        finally
        {
            foreach ((Lightning lightning, Vector2 position) in moved)
                lightning.Position = position;
        }
    }

    private static void RunAtTopologyPosition(Lightning lightning, Action action)
    {
        if (!MiaoNetModule.IsWatching
            || !metadata.TryGetValue(lightning, out Metadata? info) || !info.Moving)
        {
            action();
            return;
        }

        Vector2 position = lightning.Position;
        lightning.Position = info.Start;
        try
        {
            action();
        }
        finally
        {
            lightning.Position = position;
        }
    }

    private static void Lightning_Update(On.Celeste.Lightning.orig_Update orig, Lightning self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            Vector2 before = self.Position;
            orig(self);
            if (metadata.TryGetValue(self, out Metadata? sourceMotion) && sourceMotion.Moving)
            {
                float alongPath = Vector2.Dot(
                    self.Position - before,
                    sourceMotion.End - sourceMotion.Start
                );
                if (Math.Abs(alongPath) > 0.0001f)
                    sourceMotion.TowardEnd = alongPath > 0f;
            }
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        bool moving = metadata.TryGetValue(self, out Metadata? motion) && motion.Moving;
        if (moving)
        {
            foreach (Coroutine coroutine in self.Components.GetAll<Coroutine>())
                coroutine.Active = false;
        }
        self.Components.Update();
        self.Collidable = false;
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
            return;
        if (moving && motion!.MoveTime > 0f)
        {
            float step = Engine.DeltaTime / motion.MoveTime;
            if (applied.TowardEnd)
            {
                applied.MotionPhase += step;
                if (applied.MotionPhase >= 1f)
                {
                    applied.MotionPhase = Math.Max(0f, 2f - applied.MotionPhase);
                    applied.TowardEnd = false;
                }
            }
            else
            {
                applied.MotionPhase -= step;
                if (applied.MotionPhase <= 0f)
                {
                    applied.MotionPhase = Math.Min(1f, -applied.MotionPhase);
                    applied.TowardEnd = true;
                }
            }
            self.Position = PositionAtPhase(motion, applied.MotionPhase);
        }
        else if (applied.Elapsed < AnchorInterval)
        {
            applied.Elapsed = Math.Min(AnchorInterval, applied.Elapsed + Engine.DeltaTime);
            self.Position = Vector2.Lerp(
                applied.Start,
                applied.Target,
                applied.Elapsed / AnchorInterval
            );
        }
    }

    private static void LightningRenderer_Update(
        On.Celeste.LightningRenderer.orig_Update orig,
        LightningRenderer self
    )
    {
        orig(self);
        if (!MiaoNetModule.IsWatching || MiaoNetModule.IsWatchedPlayerPaused
            || self.Scene is not Level level
            || !rendererRemoteInfo.TryGetValue(level, out RendererRemoteInfo? info)
            || !info.Breaking)
            return;
        info.FadeElapsed = Math.Min(AnchorInterval, info.FadeElapsed + Engine.DeltaTime);
        float fade = MathHelper.Lerp(
            info.FadeStart,
            info.FadeTarget,
            info.FadeElapsed / AnchorInterval
        );
        ApplyRendererVisual(level, self, fade);
    }

    private static void Lightning_OnPlayer(
        On.Celeste.Lightning.orig_OnPlayer orig,
        Lightning self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void Lightning_Shatter(On.Celeste.Lightning.orig_Shatter orig, Lightning self)
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        orig(self);
        if (!MiaoNetModule.IsWatching && self.Scene is Level level
            && WatchEntityIDTable<Lightning>.TryGet(self, level.Session.Level, out int id))
        {
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.Lightning, id),
                ShatterEvent,
                []
            ));
        }
    }
}
