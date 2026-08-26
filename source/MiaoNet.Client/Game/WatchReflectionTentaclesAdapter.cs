using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchReflectionTentaclesAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 52;
    private const float AnchorInterval = 0.1f;
    private const byte RetreatEvent = 1;
    private const byte SnapEvent = 2;
    private const byte VisibleFlag = 1 << 0;

    private readonly record struct TentacleState(
        byte Flags,
        int Index,
        int SlideUntilIndex,
        int Layer,
        Vector2 Outwards,
        Vector2 LastOutwards,
        float Ease,
        Vector2 PlayerProjection,
        float FearDistance,
        float Offset
    );

    private sealed class Identity
    {
        public string Room { get; }
        public int EntityID { get; }
        public ushort SubID { get; }

        public Identity(string room, int entityID, ushort subID)
        {
            Room = room;
            EntityID = entityID;
            SubID = subID;
        }
    }

    private sealed class SyncInfo
    {
        private bool hasState;
        private float nextAnchorTime;
        private WatchEntityState state;

        public WatchEntityState Capture(
            Identity identity,
            TentacleState current,
            bool force,
            float time
        )
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
        public TentacleState State { get; set; }
        public Vector2 OutwardsStart { get; set; }
        public Vector2 OutwardsTarget { get; set; }
        public Vector2 LastOutwardsStart { get; set; }
        public Vector2 LastOutwardsTarget { get; set; }
        public Vector2 ProjectionStart { get; set; }
        public Vector2 ProjectionTarget { get; set; }
        public float EaseStart { get; set; }
        public float EaseTarget { get; set; }
        public float Elapsed { get; set; }
        public float Duration { get; set; }

        public void Reset(ReflectionTentacles tentacles, TentacleState state)
        {
            tentacles.outwards = OutwardsStart = OutwardsTarget = state.Outwards;
            tentacles.lastOutwards = LastOutwardsStart = LastOutwardsTarget = state.LastOutwards;
            tentacles.p = ProjectionStart = ProjectionTarget = state.PlayerProjection;
            tentacles.ease = EaseStart = EaseTarget = state.Ease;
            Elapsed = Duration = 0f;
        }

        public void Begin(ReflectionTentacles tentacles, TentacleState state)
        {
            OutwardsStart = tentacles.outwards;
            OutwardsTarget = state.Outwards;
            LastOutwardsStart = tentacles.lastOutwards;
            LastOutwardsTarget = state.LastOutwards;
            ProjectionStart = tentacles.p;
            ProjectionTarget = state.PlayerProjection;
            EaseStart = tentacles.ease;
            EaseTarget = state.Ease;
            Elapsed = 0f;
            Duration = AnchorInterval;
        }
    }

    private static readonly WatchReflectionTentaclesAdapter instance = new();
    private static readonly ConditionalWeakTable<ReflectionTentacles, Identity> identities = new();
    private static readonly ConditionalWeakTable<ReflectionTentacles, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<ReflectionTentacles, RemoteInfo> remoteInfo = new();
    private static ReflectionTentacles? creatingLayersFor;
    private static bool replayingEvent;
    private static ReflectionTentacles? initializingWatcherAwakeFor;

    public WatchEntityKind Kind => WatchEntityKind.ReflectionTentacles;

    public static void Load()
    {
        On.Celeste.ReflectionTentacles.ctor_EntityData_Vector2 += Tentacles_ctor;
        On.Celeste.ReflectionTentacles.Added += Tentacles_Added;
        On.Celeste.ReflectionTentacles.Awake += Tentacles_Awake;
        On.Celeste.ReflectionTentacles.Create += Tentacles_Create;
        On.Celeste.ReflectionTentacles.Update += Tentacles_Update;
        On.Celeste.ReflectionTentacles.Retreat += Tentacles_Retreat;
        On.Celeste.ReflectionTentacles.SnapTentacles += Tentacles_Snap;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ReflectionTentacles.SnapTentacles -= Tentacles_Snap;
        On.Celeste.ReflectionTentacles.Retreat -= Tentacles_Retreat;
        On.Celeste.ReflectionTentacles.Update -= Tentacles_Update;
        On.Celeste.ReflectionTentacles.Create -= Tentacles_Create;
        On.Celeste.ReflectionTentacles.Awake -= Tentacles_Awake;
        On.Celeste.ReflectionTentacles.Added -= Tentacles_Added;
        On.Celeste.ReflectionTentacles.ctor_EntityData_Vector2 -= Tentacles_ctor;
        identities.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        creatingLayersFor = null;
        replayingEvent = false;
        initializingWatcherAwakeFor = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (ReflectionTentacles tentacles in level.Entities.OfType<ReflectionTentacles>())
        {
            if (!identities.TryGetValue(tentacles, out Identity? identity)
                || !StringComparer.Ordinal.Equals(identity.Room, room))
                continue;
            yield return syncInfo.GetValue(tentacles, static _ => new()).Capture(
                identity,
                Capture(tentacles),
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
        Dictionary<(int EntityID, ushort SubID), TentacleState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out TentacleState value)
                || !desired.TryAdd((state.Key.EntityID, state.Key.SubID), value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (ReflectionTentacles tentacles in level.Entities.OfType<ReflectionTentacles>())
        {
            if (!identities.TryGetValue(tentacles, out Identity? identity)
                || !StringComparer.Ordinal.Equals(identity.Room, room))
                continue;
            RemoteInfo applied = remoteInfo.GetValue(tentacles, static _ => new());
            if (desired.Remove((identity.EntityID, identity.SubID), out TentacleState state))
            {
                changed |= !applied.HasState || applied.State != state;
                bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
                    || !applied.HasState || applied.State.Index != state.Index
                    || applied.State.Layer != state.Layer;
                ApplyDiscrete(tentacles, state);
                if (hard)
                    applied.Reset(tentacles, state);
                else
                    applied.Begin(tentacles, state);
                applied.State = state;
                applied.HasState = true;
            }
            else if (isCompleteState)
            {
                changed |= tentacles.Visible || applied.HasState;
                tentacles.Visible = false;
                tentacles.player = null!;
                applied.HasState = false;
            }
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        ReflectionTentacles? tentacles = Find(
            level,
            entityEvent.Key.EntityID,
            entityEvent.Key.SubID
        );
        if (tentacles is null || entityEvent.Payload.Length != 0)
            return;
        replayingEvent = true;
        try
        {
            if (entityEvent.EventID == RetreatEvent)
                tentacles.Retreat();
            else if (entityEvent.EventID == SnapEvent)
                tentacles.SnapTentacles();
        }
        finally
        {
            replayingEvent = false;
        }
    }

    private static TentacleState Capture(ReflectionTentacles tentacles)
        => new(
            tentacles.Visible ? VisibleFlag : (byte)0,
            Math.Max(0, tentacles.Index),
            tentacles.slideUntilIndex,
            Math.Clamp(tentacles.layer, 0, 3),
            tentacles.outwards,
            tentacles.lastOutwards,
            tentacles.ease,
            tentacles.p,
            tentacles.fearDistance,
            tentacles.offset
        );

    private static WatchEntityState Encode(Identity identity, TentacleState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        WatchEntityPayloadCodec.WriteInt32(payload, 4, state.Index);
        WatchEntityPayloadCodec.WriteInt32(payload, 8, state.SlideUntilIndex);
        WatchEntityPayloadCodec.WriteInt32(payload, 12, state.Layer);
        WatchEntityPayloadCodec.WriteVector2(payload, 16, state.Outwards);
        WatchEntityPayloadCodec.WriteVector2(payload, 24, state.LastOutwards);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.Ease);
        WatchEntityPayloadCodec.WriteVector2(payload, 36, state.PlayerProjection);
        WatchEntityPayloadCodec.WriteSingle(payload, 44, state.FearDistance);
        WatchEntityPayloadCodec.WriteSingle(payload, 48, state.Offset);
        return new(
            new WatchEntityKey(
                WatchEntityKind.ReflectionTentacles,
                identity.EntityID,
                identity.SubID
            ),
            payload
        );
    }

    private static bool TryDecode(WatchEntityState state, out TentacleState value)
    {
        value = default;
        ReadOnlySpan<byte> p = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.ReflectionTentacles || state.Key.SubID > 3
            || p.Length != PayloadSize || (p[0] & ~VisibleFlag) != 0
            || p[1] != 0 || p[2] != 0 || p[3] != 0)
            return false;
        int index = WatchEntityPayloadCodec.ReadInt32(p, 4);
        int slide = WatchEntityPayloadCodec.ReadInt32(p, 8);
        int layer = WatchEntityPayloadCodec.ReadInt32(p, 12);
        float[] values = new float[9];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = WatchEntityPayloadCodec.ReadSingle(p, 16 + i * 4);
            if (!float.IsFinite(values[i]))
                return false;
        }
        if (index is < 0 or > 1024 || slide is < -1 or > 1024 || layer != state.Key.SubID)
            return false;
        value = new(
            p[0],
            index,
            slide,
            layer,
            new(values[0], values[1]),
            new(values[2], values[3]),
            values[4],
            new(values[5], values[6]),
            values[7],
            values[8]
        );
        return true;
    }

    private static void ApplyDiscrete(ReflectionTentacles tentacles, TentacleState state)
    {
        tentacles.Visible = (state.Flags & VisibleFlag) != 0;
        tentacles.Index = Math.Min(state.Index, Math.Max(0, tentacles.Nodes.Count - 1));
        tentacles.slideUntilIndex = state.SlideUntilIndex;
        tentacles.layer = state.Layer;
        tentacles.fearDistance = state.FearDistance;
        tentacles.offset = state.Offset;
        tentacles.player = null!;
    }

    private static ReflectionTentacles? Find(Level level, int id, ushort subID)
        => level.Entities.OfType<ReflectionTentacles>().FirstOrDefault(tentacles =>
            identities.TryGetValue(tentacles, out Identity? identity)
            && StringComparer.Ordinal.Equals(identity.Room, level.Session.Level)
            && identity.EntityID == id && identity.SubID == subID
        );

    private static void Tentacles_ctor(
        On.Celeste.ReflectionTentacles.orig_ctor_EntityData_Vector2 orig,
        ReflectionTentacles self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        identities.AddOrUpdate(
            self,
            new Identity(data.Level.Name, data.ID, (ushort)Math.Clamp(self.layer, 0, 3))
        );
    }

    private static void Tentacles_Added(
        On.Celeste.ReflectionTentacles.orig_Added orig,
        ReflectionTentacles self,
        Scene scene
    )
    {
        ReflectionTentacles? previous = creatingLayersFor;
        if (identities.TryGetValue(self, out _))
            creatingLayersFor = self;
        try
        {
            orig(self, scene);
        }
        finally
        {
            creatingLayersFor = previous;
        }
    }

    private static void Tentacles_Awake(
        On.Celeste.ReflectionTentacles.orig_Awake orig,
        ReflectionTentacles self,
        Scene scene
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self, scene);
            return;
        }

        // Vanilla Awake advances Index by repeatedly calling Retreat while the
        // tracked Player projects inside fearDistance. Normal watcher-side
        // Retreat calls must stay blocked, but suppressing these initialization
        // calls leaves Index unchanged and turns the vanilla Awake loop into a
        // game-thread spin. Scope the exception to this exact entity and Awake.
        ReflectionTentacles? previous = initializingWatcherAwakeFor;
        initializingWatcherAwakeFor = self;
        try
        {
            orig(self, scene);
        }
        finally
        {
            initializingWatcherAwakeFor = previous;
            self.player = null!;
        }
    }

    private static void Tentacles_Create(
        On.Celeste.ReflectionTentacles.orig_Create orig,
        ReflectionTentacles self,
        float fearDistance,
        int slideUntilIndex,
        int layer,
        List<Vector2> startNodes
    )
    {
        orig(self, fearDistance, slideUntilIndex, layer, startNodes);
        if (creatingLayersFor is not null
            && identities.TryGetValue(creatingLayersFor, out Identity? parent))
            identities.AddOrUpdate(
                self,
                new Identity(parent.Room, parent.EntityID, (ushort)Math.Clamp(layer, 0, 3))
            );
    }

    private static void Tentacles_Update(
        On.Celeste.ReflectionTentacles.orig_Update orig,
        ReflectionTentacles self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.player = null!;
        self.soundDelay -= Engine.DeltaTime;
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
            return;
        if (applied.Duration > 0f)
        {
            applied.Elapsed = Math.Min(applied.Elapsed + Engine.DeltaTime, applied.Duration);
            float progress = applied.Elapsed / applied.Duration;
            self.outwards = Vector2.Lerp(applied.OutwardsStart, applied.OutwardsTarget, progress);
            self.lastOutwards = Vector2.Lerp(
                applied.LastOutwardsStart,
                applied.LastOutwardsTarget,
                progress
            );
            self.p = Vector2.Lerp(applied.ProjectionStart, applied.ProjectionTarget, progress);
            self.ease = MathHelper.Lerp(applied.EaseStart, applied.EaseTarget, progress);
        }
        AdvanceRemoteGeometry(self);
    }

    private static void AdvanceRemoteGeometry(ReflectionTentacles self)
    {
        int lastNode = self.Nodes.Count - 1;
        if (lastNode < 0)
            return;

        if (self.slideUntilIndex > self.Index)
        {
            // This is the only vanilla branch that follows Player projection.
            // The projection is authoritative state; never recompute it from
            // the Watcher's hidden Player.
            self.MoveTentacles(self.p - self.outwards * 190f);
        }
        else if (self.Index > 0)
        {
            float width = 0f;
            Vector2 node = self.Nodes[Math.Min(self.Index, lastNode)];
            float dt = Engine.DeltaTime;
            for (int i = 0; i < self.tentacleCount; i++)
            {
                ref ReflectionTentacles.Tentacle tentacle = ref self.tentacles[i];
                Vector2 target = self.TargetTentaclePosition(tentacle, node, width);
                if (tentacle.LerpPercent < 1f)
                {
                    float duration = Math.Max(tentacle.LerpDuration, 0.0001f);
                    tentacle.LerpPercent = Math.Min(1f, tentacle.LerpPercent + dt / duration);
                    tentacle.Position = Vector2.Lerp(
                        tentacle.LerpPositionFrom,
                        target,
                        Ease.CubeInOut(tentacle.LerpPercent)
                    );
                }
                else
                {
                    float response = 1f - MathF.Pow(0.1f * tentacle.Approach, dt);
                    tentacle.Position += (target - tentacle.Position) * response;
                }
                width += tentacle.Width;
            }
        }
        else
        {
            self.MoveTentacles(self.Nodes[0]);
        }

        self.UpdateVertices();
        Color vertexColor = self.color * (self.Index >= lastNode ? 1f - self.ease : 1f);
        for (int i = 0; i < self.vertices.Length; i++)
            self.vertices[i].Color = vertexColor;
    }

    private static void Tentacles_Retreat(
        On.Celeste.ReflectionTentacles.orig_Retreat orig,
        ReflectionTentacles self
    )
    {
        if (MiaoNetModule.IsWatching
            && !replayingEvent
            && !ReferenceEquals(initializingWatcherAwakeFor, self))
            return;
        orig(self);
        if (!MiaoNetModule.IsWatching)
            Publish(self, RetreatEvent);
    }

    private static void Tentacles_Snap(
        On.Celeste.ReflectionTentacles.orig_SnapTentacles orig,
        ReflectionTentacles self
    )
    {
        if (MiaoNetModule.IsWatching
            && !replayingEvent
            && !ReferenceEquals(initializingWatcherAwakeFor, self))
            return;
        orig(self);
        if (!MiaoNetModule.IsWatching)
            Publish(self, SnapEvent);
    }

    private static void Publish(ReflectionTentacles tentacles, byte eventID)
    {
        if (tentacles.Scene is Level level
            && identities.TryGetValue(tentacles, out Identity? identity)
            && StringComparer.Ordinal.Equals(identity.Room, level.Session.Level))
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(
                    new WatchEntityKey(
                        WatchEntityKind.ReflectionTentacles,
                        identity.EntityID,
                        identity.SubID
                    ),
                    eventID,
                    []
                )
            );
    }
}
