using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Reconstructs Old Site Badeline movement from the watched PlayerFrame stream.
/// Only lifecycle changes are sent through WatchEntityState; continuous position
/// is derived locally from the same delayed player history used by vanilla.
/// </summary>
internal sealed class WatchBadelineOldsiteAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 28;
    private const int MaxHistoryFrames = 4 * 60 + 4;
    private const float SourceFrameDuration = 1f / 60f;
    private const byte FallbackAnimation = 17;
    private const byte HistoryPayloadVersion = 1;
    private const int HistoryHeaderSize = 4;
    private const int HistorySampleSize = 9;
    private const int MaxHistorySamplesPerChunk = 112;
    private const float SpawnVisualDuration = 0.5f;
    private const float SpawnMoveDurationOffset = 0.1f;
    private const float PositionAnchorInterval = 0.1f;
    private const float PositionCorrectionResponse = 24f;
    private const byte HistoryAnimationMask = 0b0011_1111;
    private const byte HistoryFacingLeftFlag = 1 << 6;
    private const byte UnknownHistoryAnimation = HistoryAnimationMask;

    private const byte VisibleFlag = 1 << 0;
    private const byte FollowingFlag = 1 << 1;
    private const byte HoveringFlag = 1 << 2;
    private const byte HairVisibleFlag = 1 << 3;
    private const byte IgnorePlayerAnimationFlag = 1 << 4;
    private const byte HasOccludeFlag = 1 << 5;
    private const byte DeadLaughFlag = 1 << 6;
    private const byte ValidFlags = VisibleFlag | FollowingFlag | HoveringFlag
        | HairVisibleFlag | IgnorePlayerAnimationFlag | HasOccludeFlag | DeadLaughFlag;

    private static readonly string[] lifecycleAnimations =
    [
        "idle",
        "idleA",
        "idleB",
        "idleC",
        "lookUp",
        "walk",
        "push",
        "runSlow",
        "runFast",
        "runStumble",
        "dash",
        "dreamDashIn",
        "dreamDashLoop",
        "dreamDashOut",
        "slide",
        "jumpSlow",
        "jumpFast",
        "fallSlow",
        "fallFast",
        "tired",
        "wallslide",
        "climbLookBackStart",
        "climbLookBack",
        "climbup",
        "duck",
        "edge",
        "sleep",
        "faint",
        "fainted",
        "flip",
        "skid",
        "dangling",
        "spawn",
        "laugh",
        "angry",
        "boost",
        "pretendDead",
        "spin",
        "hug",
    ];

    private readonly record struct BadelineState(
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        byte Index,
        Vector2 Position,
        float FollowBehindTime,
        float FollowBehindIndexDelay,
        float HoveringTimer,
        int Depth
    );

    private readonly record struct LifecycleSignature(
        byte Flags,
        byte Animation,
        byte Index,
        float FollowBehindTime,
        float FollowBehindIndexDelay,
        int Depth
    );

    private readonly record struct RemotePlayerChaserState(
        Vector2 Position,
        string Animation,
        Facings Facing,
        int Depth
    );

    private readonly record struct HistoryChunk(
        byte Index,
        byte Total,
        RemotePlayerChaserState[] Samples
    );

    private readonly record struct HistoryPayloadState(
        byte Index,
        byte Total,
        RemotePlayerChaserState[] Samples
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private LifecycleSignature signature;
        private WatchEntityState state;
        private float nextPositionAnchor;

        public WatchEntityState Capture(int id, BadelineState current, bool forceCurrent, float sceneTime)
        {
            LifecycleSignature currentSignature = GetSignature(current);
            bool movingAnchor = (current.Flags & (VisibleFlag | FollowingFlag))
                == (VisibleFlag | FollowingFlag) && sceneTime >= nextPositionAnchor;
            if (forceCurrent || !hasState || currentSignature != signature || movingAnchor)
            {
                state = Encode(id, current);
                signature = currentSignature;
                hasState = true;
                nextPositionAnchor = sceneTime + PositionAnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public BadelineState State { get; set; }
        public bool Spawning { get; set; }
        public bool SpawnVisualActive { get; set; }
        public bool SpawnMoveActive { get; set; }
        public bool SpawnTargetResolved { get; set; }
        public float SpawnElapsed { get; set; }
        public float SpawnMoveDuration { get; set; }
        public Vector2 SpawnFrom { get; set; }
        public Vector2 SpawnTo { get; set; }
        public Vector2 PositionError { get; set; }
    }

    private static readonly WatchBadelineOldsiteAdapter instance = new();
    private static readonly ConditionalWeakTable<BadelineOldsite, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<BadelineOldsite, RemoteInfo> remoteInfo = new();
    private static readonly RemotePlayerChaserState[] remotePlayerHistory =
        new RemotePlayerChaserState[MaxHistoryFrames];
    private static int remotePlayerHistoryStart;
    private static int remotePlayerHistoryCount;
    private static bool remotePlayerHistorySeeded;
    private static WatchEntityState[] producerHistoryStates = [];
    private static readonly Dictionary<byte, HistoryChunk> remoteHistoryChunks = new();

    public WatchEntityKind Kind => WatchEntityKind.BadelineOldsite;

    public static void Load()
    {
        On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int += BadelineOldsite_ctor;
        On.Celeste.BadelineOldsite.Update += BadelineOldsite_Update;
        On.Celeste.BadelineOldsite.OnPlayer += BadelineOldsite_OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BadelineOldsite.OnPlayer -= BadelineOldsite_OnPlayer;
        On.Celeste.BadelineOldsite.Update -= BadelineOldsite_Update;
        On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int -= BadelineOldsite_ctor;
        WatchEntityIDTable<BadelineOldsite>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
        ResetRemotePlayerHistory();
    }

    /// <summary>
    /// Records one ordered PlayerFrame for the player currently being watched.
    /// MiaoNet already sends this stream every gameplay frame while a watcher
    /// exists, so no additional high-frequency packet is required.
    /// </summary>
    public static void RecordRemotePlayerFrame(PlayerStateDelta delta)
    {
        AppendRemotePlayerState(new(
            delta.Position,
            delta.Animation.Value,
            delta.StateFlags.HasFlag(PlayerStateFlags.FacingLeft) ? Facings.Left : Facings.Right,
            Depths.Player
        ));
    }

    private static void AppendRemotePlayerState(RemotePlayerChaserState state)
    {
        if (remotePlayerHistoryCount < MaxHistoryFrames)
        {
            int index = (remotePlayerHistoryStart + remotePlayerHistoryCount) % MaxHistoryFrames;
            remotePlayerHistory[index] = state;
            remotePlayerHistoryCount++;
        }
        else
        {
            remotePlayerHistory[remotePlayerHistoryStart] = state;
            remotePlayerHistoryStart = (remotePlayerHistoryStart + 1) % MaxHistoryFrames;
        }
    }

    public static void ResetRemotePlayerHistory()
    {
        remotePlayerHistoryStart = 0;
        remotePlayerHistoryCount = 0;
        remotePlayerHistorySeeded = false;
        remoteHistoryChunks.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        BadelineOldsite[] badelines = WatchRoomEntityIndex.Enumerate<BadelineOldsite>(level)
            .Where(badeline => WatchEntityIDTable<BadelineOldsite>.TryGet(badeline, room, out _))
            .ToArray();
        if (badelines.Length == 0)
        {
            producerHistoryStates = [];
            yield break;
        }

        if (WatchEntitySyncRegistry.IsCapturingCurrentState)
            producerHistoryStates = CaptureProducerHistory(level.Tracker.GetEntity<Player>());
        foreach (WatchEntityState historyState in producerHistoryStates)
            yield return historyState;

        foreach (BadelineOldsite badeline in badelines)
        {
            WatchEntityIDTable<BadelineOldsite>.TryGet(badeline, room, out int id);
            BadelineState current = Capture(badeline);
            yield return syncInfo.GetValue(badeline, static _ => new()).Capture(
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
        if (WatchEntitySyncRegistry.IsApplyingLifecycleReset)
            ResetRemotePlayerHistory();

        Dictionary<int, BadelineState> desired = new();
        List<HistoryChunk> historyChunks = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.SubID != 0)
            {
                if (!TryDecodeHistory(state, out HistoryChunk historyChunk))
                    return WatchEntityApplyResult.None;
                historyChunks.Add(historyChunk);
                continue;
            }
            if (!TryDecode(state, out BadelineState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }
        ApplyHistoryChunks(historyChunks);

        bool changed = false;
        string room = level.Session.Level;
        foreach (BadelineOldsite badeline in WatchRoomEntityIndex.Enumerate<BadelineOldsite>(level).ToArray())
        {
            if (!WatchEntityIDTable<BadelineOldsite>.TryGet(badeline, room, out int id))
                continue;
            RemoteInfo applied = remoteInfo.GetValue(badeline, static _ => new());
            if (desired.Remove(id, out BadelineState value))
            {
                changed |= Apply(
                    badeline,
                    value,
                    applied,
                    replayTransitions: !isCompleteState
                );
            }
            else if (isCompleteState)
            {
                changed |= badeline.Visible || badeline.Collidable || applied.HasState;
                DisableLocalSimulation(badeline, applied);
            }
        }

        foreach ((int id, BadelineState value) in desired)
        {
            BadelineOldsite badeline = new(value.Position, value.Index);
            WatchEntityIDTable<BadelineOldsite>.Set(badeline, room, id);
            level.Add(badeline);
            RemoteInfo applied = remoteInfo.GetValue(badeline, static _ => new());
            Apply(badeline, value, applied, replayTransitions: false);
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static WatchEntityState[] CaptureProducerHistory(Player? player)
    {
        if (player is null)
            return [];

        int first = Math.Max(0, player.ChaserStates.Count - MaxHistoryFrames);
        int count = player.ChaserStates.Count - first;
        int chunks = Math.Max(1, (count + MaxHistorySamplesPerChunk - 1)
            / MaxHistorySamplesPerChunk);
        WatchEntityState[] states = new WatchEntityState[chunks];
        for (int chunkIndex = 0; chunkIndex < chunks; chunkIndex++)
        {
            int chunkStart = first + chunkIndex * MaxHistorySamplesPerChunk;
            int chunkCount = Math.Min(MaxHistorySamplesPerChunk, player.ChaserStates.Count - chunkStart);
            RemotePlayerChaserState[] samples = new RemotePlayerChaserState[chunkCount];
            for (int i = 0; i < chunkCount; i++)
            {
                Player.ChaserState sample = player.ChaserStates[chunkStart + i];
                samples[i] = new(sample.Position, sample.Animation, sample.Facing, Depths.Player);
            }
            states[chunkIndex] = WatchEntityState.FromTyped(
                new WatchEntityKey(WatchEntityKind.BadelineOldsite, 0, (ushort)(chunkIndex + 1)),
                new HistoryPayloadState((byte)chunkIndex, (byte)chunks, samples),
                static state =>
                {
                    byte[] payload = new byte[
                        HistoryHeaderSize + state.Samples.Length * HistorySampleSize
                    ];
                    payload[0] = HistoryPayloadVersion;
                    payload[1] = state.Index;
                    payload[2] = state.Total;
                    payload[3] = (byte)state.Samples.Length;
                    for (int i = 0; i < state.Samples.Length; i++)
                    {
                        RemotePlayerChaserState sample = state.Samples[i];
                        int offset = HistoryHeaderSize + i * HistorySampleSize;
                        WatchEntityPayloadCodec.WriteSingle(payload, offset, sample.Position.X);
                        WatchEntityPayloadCodec.WriteSingle(payload, offset + 4, sample.Position.Y);
                        int animation = Array.IndexOf(lifecycleAnimations, sample.Animation);
                        byte packed = animation >= 0 ? (byte)animation : UnknownHistoryAnimation;
                        if (sample.Facing == Facings.Left)
                            packed |= HistoryFacingLeftFlag;
                        payload[offset + 8] = packed;
                    }
                    return payload;
                }
            );
        }
        return states;
    }

    private static bool TryDecodeHistory(WatchEntityState state, out HistoryChunk chunk)
    {
        chunk = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.BadelineOldsite
            || state.Key.EntityID != 0
            || state.Key.SubID is < 1 or > 3
            || payload.Length < HistoryHeaderSize
            || payload[0] != HistoryPayloadVersion
            || payload[1] + 1 != state.Key.SubID
            || payload[2] is < 1 or > 3
            || payload[1] >= payload[2]
            || payload[3] > MaxHistorySamplesPerChunk
            || payload.Length != HistoryHeaderSize + payload[3] * HistorySampleSize)
            return false;

        RemotePlayerChaserState[] samples = new RemotePlayerChaserState[payload[3]];
        for (int i = 0; i < samples.Length; i++)
        {
            int offset = HistoryHeaderSize + i * HistorySampleSize;
            Vector2 position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, offset),
                WatchEntityPayloadCodec.ReadSingle(payload, offset + 4)
            );
            byte packed = payload[offset + 8];
            int animation = packed & HistoryAnimationMask;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
                || (packed & 0b1000_0000) != 0
                || (animation >= lifecycleAnimations.Length
                    && animation != UnknownHistoryAnimation))
                return false;
            samples[i] = new(
                position,
                animation == UnknownHistoryAnimation ? string.Empty : lifecycleAnimations[animation],
                (packed & HistoryFacingLeftFlag) != 0 ? Facings.Left : Facings.Right,
                Depths.Player
            );
        }
        chunk = new(payload[1], payload[2], samples);
        return true;
    }

    private static void ApplyHistoryChunks(IReadOnlyCollection<HistoryChunk> chunks)
    {
        if (chunks.Count == 0)
            return;

        foreach (HistoryChunk chunk in chunks)
        {
            if (remoteHistoryChunks.Count > 0
                && remoteHistoryChunks.Values.First().Total != chunk.Total)
                remoteHistoryChunks.Clear();
            remoteHistoryChunks[chunk.Index] = chunk;
        }

        byte total = remoteHistoryChunks.Values.First().Total;
        if (remotePlayerHistorySeeded || remoteHistoryChunks.Count != total)
            return;
        for (byte i = 0; i < total; i++)
        {
            if (!remoteHistoryChunks.ContainsKey(i))
                return;
        }

        RemotePlayerChaserState[] liveTail = new RemotePlayerChaserState[remotePlayerHistoryCount];
        for (int i = 0; i < liveTail.Length; i++)
            liveTail[i] = remotePlayerHistory[(remotePlayerHistoryStart + i) % MaxHistoryFrames];
        remotePlayerHistoryStart = 0;
        remotePlayerHistoryCount = 0;
        for (byte i = 0; i < total; i++)
        {
            foreach (RemotePlayerChaserState state in remoteHistoryChunks[i].Samples)
                AppendRemotePlayerState(state);
        }
        foreach (RemotePlayerChaserState state in liveTail)
            AppendRemotePlayerState(state);
        remotePlayerHistorySeeded = true;
    }

    private static BadelineState Capture(BadelineOldsite badeline)
    {
        byte flags = 0;
        if (badeline.Visible) flags |= VisibleFlag;
        if (badeline.following) flags |= FollowingFlag;
        if (badeline.Hovering) flags |= HoveringFlag;
        if (badeline.Hair.Visible) flags |= HairVisibleFlag;
        if (badeline.ignorePlayerAnim) flags |= IgnorePlayerAnimationFlag;
        if (badeline.occlude is not null) flags |= HasOccludeFlag;
        if (badeline.player?.Dead == true) flags |= DeadLaughFlag;

        int animation = Array.IndexOf(lifecycleAnimations, badeline.Sprite.CurrentAnimationID);
        return new(
            flags,
            animation >= 0 ? (byte)animation : FallbackAnimation,
            (byte)Math.Clamp(badeline.Sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            (byte)Math.Clamp(badeline.index, 0, byte.MaxValue),
            badeline.Position,
            badeline.followBehindTime,
            badeline.followBehindIndexDelay,
            badeline.hoveringTimer,
            badeline.Depth
        );
    }

    private static LifecycleSignature GetSignature(BadelineState state)
    {
        bool followingPlayerAnimation = (state.Flags & FollowingFlag) != 0
            && (state.Flags & (IgnorePlayerAnimationFlag | DeadLaughFlag)) == 0;
        return new(
            state.Flags,
            followingPlayerAnimation ? byte.MaxValue : state.Animation,
            state.Index,
            state.FollowBehindTime,
            state.FollowBehindIndexDelay,
            followingPlayerAnimation ? Depths.Player : state.Depth
        );
    }

    private static WatchEntityState Encode(int id, BadelineState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.BadelineOldsite, id), state, PayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                payload[1] = value.Animation;
                payload[2] = value.AnimationFrame;
                payload[3] = value.Index;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteSingle(payload, 12, value.FollowBehindTime);
                WatchEntityPayloadCodec.WriteSingle(payload, 16, value.FollowBehindIndexDelay);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, value.HoveringTimer);
                WatchEntityPayloadCodec.WriteInt32(payload, 24, value.Depth);
            }
        );

    private static bool TryDecode(WatchEntityState state, out BadelineState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.BadelineOldsite
            || state.Key.SubID != 0
            || payload.Length != PayloadSize
            || (payload[0] & ~ValidFlags) != 0
            || payload[1] >= lifecycleAnimations.Length)
            return false;

        Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
        float followBehindTime = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        float followBehindIndexDelay = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        float hoveringTimer = WatchEntityPayloadCodec.ReadSingle(payload, 20);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(followBehindTime) || followBehindTime is < 0f or > 4f
            || !float.IsFinite(followBehindIndexDelay) || followBehindIndexDelay is < 0f or > 4f
            || followBehindTime + followBehindIndexDelay > 4f
            || !float.IsFinite(hoveringTimer))
            return false;

        value = new(
            payload[0],
            payload[1],
            payload[2],
            payload[3],
            position,
            followBehindTime,
            followBehindIndexDelay,
            hoveringTimer,
            WatchEntityPayloadCodec.ReadInt32(payload, 24)
        );
        return true;
    }

    private static bool Apply(
        BadelineOldsite badeline,
        BadelineState state,
        RemoteInfo applied,
        bool replayTransitions
    )
    {
        bool visible = (state.Flags & VisibleFlag) != 0;
        bool wasVisible = applied.HasState && (applied.State.Flags & VisibleFlag) != 0;
        bool beginSpawn = replayTransitions && applied.HasState && !wasVisible && visible;
        bool lifecycleChanged = !applied.HasState
            || GetSignature(applied.State) != GetSignature(state);
        bool changed = !applied.HasState || applied.State != state
            || badeline.Position != state.Position
            || badeline.Visible != visible
            || badeline.Collidable;

        if (lifecycleChanged || WatchEntitySyncRegistry.IsApplyingLifecycleReset)
        {
            badeline.Position = state.Position;
            applied.PositionError = Vector2.Zero;
        }
        else
        {
            applied.PositionError = state.Position - badeline.Position;
        }
        badeline.Visible = visible;
        badeline.Collidable = false;
        badeline.player = null;
        badeline.following = false;
        badeline.ignorePlayerAnim = (state.Flags & IgnorePlayerAnimationFlag) != 0;
        badeline.followBehindTime = state.FollowBehindTime;
        badeline.followBehindIndexDelay = state.FollowBehindIndexDelay;
        badeline.Hovering = (state.Flags & HoveringFlag) != 0;
        badeline.hoveringTimer = state.HoveringTimer;
        badeline.Hair.Visible = (state.Flags & HairVisibleFlag) != 0;
        badeline.Depth = state.Depth;
        EnsureOcclude(badeline, (state.Flags & HasOccludeFlag) != 0);
        ApplyLifecycleAnimation(badeline, state, lifecycleChanged);

        if (beginSpawn)
        {
            BeginRemoteSpawn(badeline, state, applied);
        }
        else if (!visible || !replayTransitions)
        {
            FinishRemoteSpawn(badeline, applied);
        }
        else if (applied.Spawning && (state.Flags & FollowingFlag) != 0)
        {
            // Vanilla enables following only after its entry position tween ends.
            applied.SpawnMoveActive = false;
        }

        applied.State = state;
        applied.HasState = true;
        return changed;
    }

    private static void DisableLocalSimulation(BadelineOldsite badeline, RemoteInfo applied)
    {
        badeline.Visible = false;
        badeline.Collidable = false;
        badeline.player = null;
        badeline.following = false;
        FinishRemoteSpawn(badeline, applied);
        applied.HasState = false;
        applied.PositionError = Vector2.Zero;
    }

    private static void BeginRemoteSpawn(
        BadelineOldsite badeline,
        BadelineState state,
        RemoteInfo applied
    )
    {
        applied.Spawning = true;
        applied.SpawnVisualActive = true;
        applied.SpawnMoveActive = true;
        applied.SpawnTargetResolved = false;
        applied.SpawnElapsed = 0f;
        applied.SpawnMoveDuration = Math.Max(0f, state.FollowBehindTime - SpawnMoveDurationOffset);
        applied.SpawnFrom = state.Position;
        applied.SpawnTo = state.Position;

        if (TryGetChaseState(state.FollowBehindIndexDelay, out RemotePlayerChaserState target))
        {
            applied.SpawnTo = target.Position;
            applied.SpawnTargetResolved = true;
        }

        badeline.Position = applied.SpawnFrom;
        badeline.Sprite.Scale = Vector2.Zero;
        badeline.Sprite.Color = Color.Transparent;
        badeline.Hair.Visible = true;
        badeline.Hair.Alpha = 0f;
    }

    private static void UpdateRemoteSpawn(
        BadelineOldsite badeline,
        BadelineState state,
        RemoteInfo applied,
        float deltaTime
    )
    {
        if (!applied.Spawning)
            return;

        applied.SpawnElapsed += deltaTime;
        if (applied.SpawnMoveActive && !applied.SpawnTargetResolved
            && TryGetChaseState(
                state.FollowBehindIndexDelay + applied.SpawnElapsed,
                out RemotePlayerChaserState target
            ))
        {
            applied.SpawnTo = target.Position;
            applied.SpawnTargetResolved = true;
        }

        if (applied.SpawnVisualActive)
        {
            float visualProgress = Ease.CubeIn(Math.Min(
                1f,
                applied.SpawnElapsed / SpawnVisualDuration
            ));
            badeline.Sprite.Scale = Vector2.One * visualProgress;
            badeline.Sprite.Color = Color.White * visualProgress;
            badeline.Hair.Alpha = visualProgress;
            if (applied.SpawnElapsed >= SpawnVisualDuration)
            {
                applied.SpawnVisualActive = false;
                NormalizeRemoteSpawnAppearance(badeline);
            }
        }

        if (applied.SpawnMoveActive && applied.SpawnTargetResolved)
        {
            float moveProgress = applied.SpawnMoveDuration <= 0f
                ? 1f
                : Math.Min(1f, applied.SpawnElapsed / applied.SpawnMoveDuration);
            badeline.Position = Vector2.Lerp(
                applied.SpawnFrom,
                applied.SpawnTo,
                Ease.CubeIn(moveProgress)
            );
            if (applied.SpawnTo.X != applied.SpawnFrom.X)
            {
                badeline.Sprite.Scale.X = Math.Abs(badeline.Sprite.Scale.X)
                    * Math.Sign(applied.SpawnTo.X - applied.SpawnFrom.X);
            }
            badeline.Trail();
            if (moveProgress >= 1f)
                applied.SpawnMoveActive = false;
        }

        if (applied.SpawnMoveActive && applied.SpawnElapsed >= applied.SpawnMoveDuration)
            applied.SpawnMoveActive = false;
        if (!applied.SpawnVisualActive && !applied.SpawnMoveActive)
            FinishRemoteSpawn(badeline, applied);
    }

    private static void FinishRemoteSpawn(BadelineOldsite badeline, RemoteInfo applied)
    {
        applied.Spawning = false;
        applied.SpawnVisualActive = false;
        applied.SpawnMoveActive = false;
        applied.SpawnTargetResolved = false;
        applied.SpawnElapsed = 0f;
        NormalizeRemoteSpawnAppearance(badeline);
    }

    private static void NormalizeRemoteSpawnAppearance(BadelineOldsite badeline)
    {
        float facing = badeline.Sprite.Scale.X < 0f ? -1f : 1f;
        badeline.Sprite.Scale = new Vector2(facing, 1f);
        badeline.Sprite.Color = Color.White;
        badeline.Hair.Alpha = 1f;
    }

    private static void EnsureOcclude(BadelineOldsite badeline, bool enabled)
    {
        if (enabled && badeline.occlude is null)
        {
            badeline.occlude = new LightOcclude(1f);
            badeline.Add(badeline.occlude);
        }
        if (badeline.occlude is not null)
            badeline.occlude.Visible = enabled;
    }

    private static void ApplyLifecycleAnimation(
        BadelineOldsite badeline,
        BadelineState state,
        bool alignFrame
    )
    {
        string animation = lifecycleAnimations[state.Animation];
        if (badeline.Sprite.CurrentAnimationID != animation)
            badeline.Sprite.Play(animation, restart: true);
        if (alignFrame && badeline.Sprite.CurrentAnimationTotalFrames > 0)
        {
            badeline.Sprite.SetAnimationFrame(Math.Min(
                state.AnimationFrame,
                badeline.Sprite.CurrentAnimationTotalFrames - 1
            ));
        }
    }

    private static bool TryGetChaseState(float delay, out RemotePlayerChaserState state)
    {
        int framesBehind = (int)MathF.Floor(delay / SourceFrameDuration + 0.0001f);
        int index = remotePlayerHistoryCount - 1 - framesBehind;
        // Vanilla requires a sample on both sides of the requested delay and
        // accepts only a gap below 0.02 seconds. Consecutive 60 Hz PlayerFrames
        // satisfy the same contract once index is greater than zero.
        if (index <= 0)
        {
            state = default;
            return false;
        }
        state = remotePlayerHistory[(remotePlayerHistoryStart + index) % MaxHistoryFrames];
        return true;
    }

    private static void BadelineOldsite_ctor(
        On.Celeste.BadelineOldsite.orig_ctor_EntityData_Vector2_int orig,
        BadelineOldsite self,
        EntityData data,
        Vector2 offset,
        int index
    )
    {
        orig(self, data, offset, index);
        WatchEntityIDTable<BadelineOldsite>.Set(self, data.Level.Name, data.ID);
    }

    private static void BadelineOldsite_Update(
        On.Celeste.BadelineOldsite.orig_Update orig,
        BadelineOldsite self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        if (!remoteInfo.TryGetValue(self, out RemoteInfo? applied) || !applied.HasState)
        {
            self.Visible = false;
            self.Collidable = false;
            return;
        }

        BadelineState lifecycle = applied.State;
        self.Visible = (lifecycle.Flags & VisibleFlag) != 0;
        self.Collidable = false;

        float deltaTime = Engine.DeltaTime;
        bool deadLaugh = (lifecycle.Flags & DeadLaughFlag) != 0;
        bool following = (lifecycle.Flags & FollowingFlag) != 0;
        bool hovering = (lifecycle.Flags & HoveringFlag) != 0;

        UpdateRemoteSpawn(self, lifecycle, applied, deltaTime);

        if (deadLaugh)
        {
            if (self.Sprite.CurrentAnimationID != "laugh")
                self.Sprite.Play("laugh");
            self.Sprite.X = MathF.Sin(self.hoveringTimer) * 4f;
            hovering = true;
            self.hoveringTimer += deltaTime * 2f;
            self.Depth = -12500;
            self.Trail();
        }
        else if (following && TryGetChaseState(
            lifecycle.FollowBehindTime + lifecycle.FollowBehindIndexDelay,
            out RemotePlayerChaserState chaseState))
        {
            self.Position = Calc.Approach(self.Position, chaseState.Position, 500f * deltaTime);
            if (!self.ignorePlayerAnim
                && self.Sprite.CurrentAnimationID != chaseState.Animation
                && chaseState.Animation is not null
                && self.Sprite.Has(chaseState.Animation))
                self.Sprite.Play(chaseState.Animation, restart: true);
            if (!self.ignorePlayerAnim)
                self.Sprite.Scale.X = Math.Abs(self.Sprite.Scale.X) * (float)chaseState.Facing;
            self.Depth = chaseState.Depth;
            self.Trail();
        }

        float correction = 1f - MathF.Exp(-PositionCorrectionResponse * deltaTime);
        self.Position += applied.PositionError * correction;
        applied.PositionError *= 1f - correction;

        if (self.Sprite.Scale.X != 0f)
            self.Hair.Facing = (Facings)Math.Sign(self.Sprite.Scale.X);
        if (hovering)
        {
            self.hoveringTimer += deltaTime;
            self.Sprite.Y = MathF.Sin(self.hoveringTimer * 2f) * 4f;
        }
        else
        {
            self.Sprite.X = Calc.Approach(self.Sprite.X, 0f, deltaTime * 4f);
            self.Sprite.Y = Calc.Approach(self.Sprite.Y, 0f, deltaTime * 4f);
        }

        if (self.occlude is not null)
            self.occlude.Visible = (lifecycle.Flags & HasOccludeFlag) != 0
                && !self.CollideCheck<Solid>();
        self.Sprite.Update();
        self.Hair.Update();
    }

    private static void BadelineOldsite_OnPlayer(
        On.Celeste.BadelineOldsite.orig_OnPlayer orig,
        BadelineOldsite self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }
}
