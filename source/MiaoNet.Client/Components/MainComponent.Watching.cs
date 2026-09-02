using System.Globalization;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private const int WatchPlaybackQueueCapacity = 512;
    private static readonly long WatchPlaybackDelayTicks =
        WatchPlaybackTiming.GetDelayTicks(System.Diagnostics.Stopwatch.Frequency);

    private readonly record struct WatchPlayerFrameSample(
        uint PlayerEpoch,
        uint PlayerSequence,
        PlayerLocation Location,
        PlayerFrameKind Kind,
        PlayerStateDelta Delta,
        PlayerState? KeyframeState
    );

    private enum WatchPlayerPresentationEventKind
    {
        PauseChanged,
        LiveState,
        TargetRestarting,
    }

    private readonly record struct WatchPlayerPresentationEvent(
        WatchPlayerPresentationEventKind Kind,
        uint PlayerEpoch,
        uint PlayerSequence,
        bool Paused,
        LiveStateType LiveState,
        Vector2 Value,
        WatchTargetRestartKind RestartKind = default
    );

    private int? watchSessionID;
    private HashSet<string>? watchBaselineFlags;
    private PlayerMapLocation watchMap;
    private int lastWatchSequence;
    private int lastWatchReceivedSequence;
    private bool watchResyncPending;
    private WatchSceneSnapshot? pendingWatchResyncSnapshot;
    private PlayerLocation watchEntityLocation;
    private Dictionary<WatchEntityKey, WatchEntityState>? watchEntityStates;
    private readonly HashSet<WatchEntityKey> watchPendingEntityStateKeys = new();
    private readonly List<WatchEntityEvent> watchPendingEntityEvents = new();
    private WatchEntityStateMode watchPendingEntityStateMode;
    private bool watchEntityLifecycleResetPending;
    private readonly HashSet<WatchEntityKind> watchLifecycleIncompleteKinds = new();
    private bool watchEntityStateApplied;
    private WatchPersistentSessionBaseline? watchPersistentSessionBaseline;
    private bool watchRoomReloadPending;
    private PlayerLocation watchRoomReloadLocation;
    private bool watchSceneRestorePending;
    private PlayerLocation watchSceneRestoreLocation;
    private bool watchRoomTransitionPending;
    private WatchRoomTransition? watchRoomTransition;
    private PlayerLocation watchCameraLocation;
    private Vector2? watchCameraTarget;
    private bool watchCameraAwaitingFreshSample;
    private bool watchCameraApplyAfterLevelUpdate;
    private readonly WatchPlaybackQueue<WatchPlayerFrameSample> watchPlayerFrameBuffer =
        new(WatchPlaybackQueueCapacity);
    private WatchPlaybackEntry<WatchPlayerFrameSample>? watchCurrentPlayerFrame;
    private PlayerState? watchPlaybackPlayerState;
    private uint watchPlayerEpoch;
    private WatchReceivedEpochTracker watchReceivedPlayerEpoch;
    private uint watchPlaybackPlayerSequence;
    private bool watchPlaybackAwaitingKeyframe;
    private PlayerLocation watchPlaybackLocation;
    private bool watchPlaybackPaused;
    private readonly WatchPlaybackQueue<WatchSceneDelta> watchSceneDeltaBuffer =
        new(WatchPlaybackQueueCapacity);
    private readonly WatchPlaybackQueue<WatchPlayerPresentationEvent> watchPlayerEventBuffer =
        new(WatchPlaybackQueueCapacity);
    private PlayerLocation watchReceivedEntityLocation;
    private bool watchTargetRestartPending;
    private bool watchTargetRestartSuspended;
    private WatchTargetRestartKind? watchTargetRestartKind;

    public bool WatchRequestPending { get; private set; }

    public bool CanStartWatching
        => !WatchRequestPending && !Watching && !watchSceneRestorePending;

    private void CleanUpWatching()
    {
        WatchRequestPending = false;
        StopWatching(false);
        ClearWatchSceneProducer();
    }

    private void UpdateWatching(Level level, Player player)
    {
        watchCameraApplyAfterLevelUpdate = false;
        if (playerWatching is not null)
        {
            if (playerWatching.State is null
                && !watchTargetRestartPending
                && !watchTargetRestartSuspended)
            {
                StopWatching();
                return;
            }

            if (playerWatching.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching))
            {
                context.ChatComponent.AddLocalChat(MiaoNetChatText.CreateCommandTip(
                    PFormat.Format(
                        CultureInfo.CurrentCulture,
                        Dialog.Get("miaonet_commands_watch_others_watching"),
                        playerWatching.Info.Name
                    )
                ));
                StopWatching();
                return;
            }

            if (watchSessionID is null)
            {
                UpdateLegacyWatching(level, player);
                return;
            }

            ApplyPendingWatchResyncSnapshot(level);

            player.Visible = false;
            player.StateMachine.State = Player.StFrozen;
            if (watchTargetRestartSuspended)
            {
                if (UpdateWatchDeathTransition(level))
                    ApplyPendingWatchEntityEvents(level);
                return;
            }
            if (watchResyncPending)
                return;

            AdvanceWatchPlayback(level);
            if (watchResyncPending)
                return;

            if (watchRoomTransitionPending)
            {
                if (level.transition is not null)
                {
                    // Level.TransitionTo owns the camera until its coroutine finishes.
                    // The target room is loaded near the beginning of that coroutine,
                    // so reconcile its scene while the camera is still moving into it.
                    ApplyWatchEntityState(level, allowDuringTransition: true);
                    return;
                }

                CompleteWatchRoomTransition(level, player);
            }

            if (UpdateWatchDeathTransition(level))
            {
                ApplyPendingWatchEntityEvents(level);
                return;
            }

            PlayerLocation localPlayerLocation = PlayerLocation.FetchFrom(level.Session);
            PlayerLocation watchedPlayerLocation = watchPlaybackLocation;
            if (localPlayerLocation.Room != watchedPlayerLocation.Room
                && !watchedPlayerLocation.IsInDebugMap
                && level.transition is null)
            {
                // Wait for the ordered room Replace. Player location packets can
                // arrive before the scene delta carrying the authoritative vanilla
                // transition direction and target-room entity snapshot.
                if (watchEntityLocation != watchedPlayerLocation
                    || watchPendingEntityStateMode != WatchEntityStateMode.Replace)
                    return;
                BeginWatchRoomTransition(level);
                return;
            }

            if (UpdateWatchRoomReload(level))
                return;

            ApplyWatchEntityState(level);
            player.Visible = false;
            player.StateMachine.State = Player.StFrozen;

            if (level.InCutscene && !level.SkippingCutscene)
                level.SkipCutscene();

            // Level entities and CameraTargetTriggers update after the network
            // component. Defer the authoritative sample until Level.Update has
            // finished so room-local camera logic cannot overwrite it.
            watchCameraApplyAfterLevelUpdate = true;
        }
    }

    private void UpdateLegacyWatching(Level level, Player player)
    {
        SafeGuard.Assert(playerWatching?.State is not null);
        PlayerLocation selfLocation = PlayerLocation.FetchFrom(level.Session);
        PlayerLocation targetLocation = playerWatching!.Location;
        if (selfLocation.Room != targetLocation.Room
            && !targetLocation.IsInDebugMap
            && level.transition is null)
        {
            Vector2 targetPosition = playerWatching.State!.Position;
            LevelData room = level.Session.MapData.Get(targetLocation.Room);
            Vector2 spawn = room.Spawns.ClosestTo(targetPosition);
            level.Session.RespawnPoint = spawn;
            Vector2 previousPosition = player.Position;
            player.Position = spawn;
            level.TransitionTo(room, (player.Position - previousPosition).SafeNormalize());
        }

        player.Visible = false;
        player.StateMachine.State = Player.StFrozen;
        if (level.InCutscene && !level.SkippingCutscene)
            level.SkipCutscene();

        Vector2 cameraTarget = GetWatchCameraTarget(level, playerWatching.State!.Position);
        level.Camera.Position = Calc.Approach(
            level.Camera.Position,
            cameraTarget,
            (level.Camera.Position - cameraTarget).Length() * 4f * Engine.RawDeltaTime
        );
    }

    public bool TryBeginWatchRequest()
    {
        if (!CanStartWatching)
            return false;

        WatchRequestPending = true;
        return true;
    }

    public bool CompleteWatchRequest()
    {
        bool wasPending = WatchRequestPending;
        WatchRequestPending = false;
        return wasPending;
    }

    public bool CancelWatchRequest()
    {
        bool wasPending = WatchRequestPending;
        WatchRequestPending = false;
        return wasPending;
    }

    public bool StartLegacyWatching(OnlinePlayer player)
    {
        if (!CanStartWatching
            || Engine.Scene is not Level level
            || player.State is null
            || player.Channel != ClientState.Self.Channel
            || player.Location.Map != PlayerLocation.FetchFrom(level.Session).Map)
            return false;

        playerWatching = player;
        if (ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost))
            ghost.SetWatchFocus(true);
        Logger.Info(LT.MiaoNetWatch, $"Legacy watch started for player {player.ID}.");
        return true;
    }

    public bool StartWatching(OnlinePlayer player, int sessionID, WatchSceneSnapshot snapshot)
    {
        if (Engine.Scene is not Level level
            || snapshot.Location.Map != ClientState.Self.Location.Map
            || snapshot.Location.Map != player.Location.Map)
            return false;

        watchBaselineFlags = new(level.Session.Flags, StringComparer.Ordinal);
        watchPersistentSessionBaseline = WatchPersistentSessionBaseline.Capture(level.Session);
        WatchPersistentSessionAdapter.ResetRemoteState(level);
        WatchRoomEnvironmentAdapter.CaptureBaseline(level);
        watchSessionID = sessionID;
        playerWatching = player;
        WatchTriggerFirewall.BeginWatching(level);
        WatchBadelineOldsiteAdapter.ResetRemotePlayerHistory();
        WatchAngryOshiroAdapter.ResetRemotePlayerState();
        WatchPlayerSeekerAdapter.ResetRemoteState();
        if (ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost))
            ghost.SetWatchFocus(true);
        ApplyWatchSnapshot(level, snapshot, false);

        Logger.Info(
            LT.MiaoNetWatch,
            $"Watch session {sessionID} started for player {player.ID}; " +
            $"snapshot flags={snapshot.Flags.Count}, " +
            $"entities={snapshot.EntityStates.Count}, sequence={snapshot.Sequence}."
        );
        return true;
    }

    private void ApplyWatchSnapshot(Level level, WatchSceneSnapshot snapshot, bool isResync)
    {
        bool completesTargetRestart = isResync && watchTargetRestartSuspended;
        ReplaceFlags(level.Session.Flags, snapshot.Flags);
        watchMap = snapshot.Location.Map;
        lastWatchSequence = snapshot.Sequence;
        lastWatchReceivedSequence = snapshot.Sequence;
        watchReceivedPlayerEpoch.Reset(snapshot.PlayerEpoch);
        watchEntityLocation = snapshot.Location;
        watchReceivedEntityLocation = snapshot.Location;
        watchSceneDeltaBuffer.Clear();
        watchPlayerEventBuffer.Clear();
        if (playerWatching is { } watchedPlayer)
            ResetWatchPlayerPlayback(
                watchedPlayer,
                snapshot.Location,
                snapshot.PlayerEpoch,
                snapshot.PlayerSequenceWatermark
            );
        watchEntityStates = snapshot.EntityStates.ToDictionary(state => state.Key);
        watchPendingEntityStateKeys.Clear();
        watchPendingEntityStateKeys.UnionWith(watchEntityStates.Keys);
        watchPendingEntityEvents.Clear();
        watchPendingEntityStateMode = WatchEntityStateMode.Replace;
        watchEntityLifecycleResetPending = isResync;
        watchLifecycleIncompleteKinds.Clear();
        if (!isResync)
            watchEntityStateApplied = false;
        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;
        if (level.transition is null)
            watchRoomTransitionPending = false;
        watchRoomTransition = null;
        watchResyncPending = false;
        pendingWatchResyncSnapshot = null;
        InvalidateBufferedWatchCamera(awaitFreshSample: isResync);
        if (completesTargetRestart)
        {
            watchTargetRestartSuspended = false;
            if (playerWatching?.State is { } state)
            {
                BufferWatchRespawnNotification(
                    state.Position,
                    watchTargetRestartKind == WatchTargetRestartKind.RestartChapter
                );
            }
            MarkWatchDeathRespawnStateReady(snapshot.Location);
        }
        else
            CancelWatchDeathTransition(level);
    }

    public OnlinePlayer? StopWatching(bool notifyServer = true)
    {
        OnlinePlayer? player = playerWatching;
        var level = Engine.Scene as Level ?? (Engine.Scene as AssetReloadHelper)?.OrigScene as Level;
        bool loadUnloadedDeathRoom = IsWatchDeathRoomUnloaded;
        CancelWatchRoomTransition(level);
        if (!loadUnloadedDeathRoom)
            CancelWatchDeathTransition(level);
        if (player is not null && ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost))
            ghost.SetWatchFocus(false);
        playerWatching = null;
        WatchBadelineOldsiteAdapter.ResetRemotePlayerHistory();
        WatchAngryOshiroAdapter.ResetRemotePlayerState();
        WatchPlayerSeekerAdapter.ResetRemoteState();
        if (level is not null)
            WatchPersistentSessionAdapter.ResetRemoteState(level);

        if (watchSessionID is int sessionID)
        {
            if (notifyServer && context.HasConnection)
                context.QueuePacket(new PacketWatchStop(sessionID));
            Logger.Info(LT.MiaoNetWatch, $"Watch session {sessionID} stopped; notifyServer={notifyServer}.");
        }

        bool restoreScene = watchEntityStateApplied;
        PlayerLocation restoreLocation = watchEntityLocation;

        watchSessionID = null;
        lastWatchSequence = 0;
        lastWatchReceivedSequence = 0;
        watchReceivedPlayerEpoch.Reset();
        watchResyncPending = false;
        pendingWatchResyncSnapshot = null;
        watchTargetRestartPending = false;
        watchTargetRestartSuspended = false;
        watchTargetRestartKind = null;
        watchMap = default;
        watchEntityLocation = default;
        watchReceivedEntityLocation = default;
        watchSceneDeltaBuffer.Clear();
        watchPlayerEventBuffer.Clear();
        watchEntityStates = null;
        watchPendingEntityStateKeys.Clear();
        watchPendingEntityEvents.Clear();
        watchPendingEntityStateMode = WatchEntityStateMode.None;
        watchEntityLifecycleResetPending = false;
        watchLifecycleIncompleteKinds.Clear();
        watchEntityStateApplied = false;
        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;
        watchRoomTransition = null;
        watchCameraLocation = default;
        watchCameraTarget = null;
        watchCameraAwaitingFreshSample = false;
        ClearWatchPlayerPlayback();

        if (level is not null && watchBaselineFlags is not null)
            ReplaceFlags(level.Session.Flags, watchBaselineFlags);
        watchBaselineFlags = null;
        if (level is not null && watchPersistentSessionBaseline is not null)
        {
            watchPersistentSessionBaseline.Restore(level.Session);
            level.CoreMode = level.Session.CoreMode;
        }
        watchPersistentSessionBaseline = null;
        if (loadUnloadedDeathRoom)
            CancelWatchDeathTransition(level);
        if (level is not null)
            WatchRoomEnvironmentAdapter.RestoreBaseline(level);
        if (restoreScene && level is not null)
        {
            watchSceneRestorePending = true;
            watchSceneRestoreLocation = restoreLocation;
        }

        if (player is not null)
        {
            var playerEntity = level?.Tracker.GetEntity<Player>();
            if (playerEntity is not null)
            {
                playerEntity.Visible = true;
                playerEntity.StateMachine.State = Player.StNormal;
                playerEntity.ForceCameraUpdate = false;
            }
        }

        ReleaseWatchRoomEntityIndexIfUnused();
        return player;
    }

    private void Context_WatchSceneDeltaReceived(PacketWatchSceneDeltaNotification packet)
    {
        if (watchSessionID != packet.SessionID
            || playerWatching?.ID != packet.TargetPlayerID)
            return;

        if (packet.Delta.Sequence <= lastWatchReceivedSequence
            || watchResyncPending
            || watchTargetRestartPending
            || watchTargetRestartSuspended)
            return;

        if (!watchReceivedPlayerEpoch.CanAccept(
            packet.Delta.PlayerEpoch,
            packet.Delta.EntityStateMode
        ))
        {
            BeginWatchResync(packet.Delta.Sequence, "player epoch mismatch");
            return;
        }

        if (packet.Delta.Sequence != lastWatchReceivedSequence + 1)
        {
            BeginWatchResync(packet.Delta.Sequence, "sequence gap");
            return;
        }

        if (Engine.Scene is not Level level
            || PlayerLocation.FetchFrom(level.Session).Map != watchMap
            || packet.Delta.Location.Map != watchMap
            || (packet.Delta.EntityStateMode == WatchEntityStateMode.Patch
                && watchReceivedEntityLocation != packet.Delta.Location))
        {
            BeginWatchResync(packet.Delta.Sequence, "scene mismatch");
            return;
        }

        WatchPlaybackEnqueueResult enqueueResult = watchSceneDeltaBuffer.Enqueue(
            context.CurrentReceivedPacketTimestamp,
            packet.Delta
        );
        if (enqueueResult != WatchPlaybackEnqueueResult.Success)
        {
            BeginWatchResync(packet.Delta.Sequence, $"scene playback buffer {enqueueResult}");
            return;
        }

        lastWatchReceivedSequence = packet.Delta.Sequence;
        if (packet.Delta.EntityStateMode == WatchEntityStateMode.Replace)
        {
            watchReceivedPlayerEpoch.RecordAccepted(
                packet.Delta.PlayerEpoch,
                packet.Delta.EntityStateMode
            );
            watchReceivedEntityLocation = packet.Delta.Location;
        }
    }

    private void ApplyBufferedWatchSceneDelta(Level level, WatchSceneDelta delta)
    {
        if (delta.PlayerEpoch > watchPlayerEpoch)
        {
            if (delta.EntityStateMode != WatchEntityStateMode.Replace
                || playerWatching is not { } watchedPlayer)
            {
                BeginWatchResync(delta.Sequence, "epoch changed without Replace");
                return;
            }

            watchPlayerEpoch = delta.PlayerEpoch;
            watchPlaybackPlayerSequence = 0;
            watchPlaybackAwaitingKeyframe = false;
            watchCurrentPlayerFrame = null;
            watchPlaybackPlayerState = watchedPlayer.EpochBaselineState?.Clone();
            watchPlayerEventBuffer.Clear();
            InvalidateBufferedWatchCamera(awaitFreshSample: true);
            HandleLocationChanging(level, watchedPlayer);
        }

        delta.ApplyTo(level.Session.Flags);
        if (watchRoomReloadPending && watchRoomReloadLocation != delta.Location)
        {
            watchRoomReloadPending = false;
            watchRoomReloadLocation = default;
        }
        if (delta.EntityStateMode == WatchEntityStateMode.Replace)
        {
            watchEntityLocation = delta.Location;
            watchEntityStates ??= new();
            watchEntityStates.Clear();
            foreach (WatchEntityState state in delta.EntityStates)
                watchEntityStates.Add(state.Key, state);
            watchPendingEntityStateKeys.Clear();
            watchPendingEntityStateKeys.UnionWith(watchEntityStates.Keys);
            // A death-respawn Replace arrives after the event that caused the
            // death. Consume that event against the still-live room before the
            // black-frame snapshot replaces the lifecycle; otherwise a
            // Snowball impact can be discarded without ever being rendered.
            if (delta.IsDeathRespawn)
            {
                if (IsWatchDeathRoomUnloaded)
                    watchPendingEntityEvents.Clear();
                else
                    ApplyPendingWatchEntityEvents(level);
            }
            else
                watchPendingEntityEvents.Clear();
            watchPendingEntityStateMode = WatchEntityStateMode.Replace;
            watchEntityLifecycleResetPending = delta.IsDeathRespawn;
            if (delta.IsDeathRespawn)
                watchLifecycleIncompleteKinds.Clear();
            watchRoomTransition = delta.RoomTransition;
        }
        else if (delta.EntityStateMode == WatchEntityStateMode.Patch)
        {
            watchEntityStates ??= new();
            foreach (WatchEntityState state in delta.EntityStates)
            {
                watchEntityStates[state.Key] = state;
                watchPendingEntityStateKeys.Add(state.Key);
            }
            if (watchPendingEntityStateMode == WatchEntityStateMode.None)
                watchPendingEntityStateMode = WatchEntityStateMode.Patch;
        }
        watchPendingEntityEvents.AddRange(delta.EntityEvents);
        if (WatchSceneLifecyclePolicy.AuthorizesRoomReload(delta))
        {
            watchRoomReloadPending = true;
            watchRoomReloadLocation = delta.Location;
        }
        if (delta.IsDeathRespawn)
            MarkWatchDeathRespawnStateReady(delta.Location);
        watchPlaybackLocation = delta.Location;
        lastWatchSequence = delta.Sequence;
    }

    private void BeginWatchResync(int receivedSequence, string reason)
    {
        if (watchSessionID is not int sessionID || watchResyncPending)
            return;

        watchResyncPending = true;
        pendingWatchResyncSnapshot = null;
        watchSceneDeltaBuffer.Clear();
        watchPlayerEventBuffer.Clear();
        watchPlayerFrameBuffer.Clear();
        watchCurrentPlayerFrame = null;
        watchReceivedPlayerEpoch.Reset(watchPlayerEpoch);
        context.QueuePacket(new PacketWatchResyncRequest(sessionID, lastWatchSequence));
        Logger.Warn(
            LT.MiaoNetWatch,
            $"Paused watch session {sessionID} after {reason}; " +
            $"last={lastWatchSequence}, received={receivedSequence}, requesting a snapshot."
        );
    }

    private void Context_WatchResyncSnapshotReceived(PacketWatchResyncSnapshot packet)
    {
        if (watchSessionID != packet.SessionID || playerWatching?.ID != packet.TargetPlayerID)
            return;

        if (packet.Snapshot.Location.Map != watchMap
            || packet.Snapshot.Sequence < lastWatchSequence
            || !WatchPacketValidator.IsValid(packet.Snapshot))
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Rejected watch resync snapshot for session {packet.SessionID}, " +
                $"target {packet.TargetPlayerID}, sequence {packet.Snapshot.Sequence}."
            );
            watchResyncPending = false;
            pendingWatchResyncSnapshot = null;
            return;
        }

        if (!watchTargetRestartPending)
            watchResyncPending = true;
        pendingWatchResyncSnapshot = packet.Snapshot;
        if (!watchTargetRestartPending && Engine.Scene is Level level)
            ApplyPendingWatchResyncSnapshot(level);
    }

    private void ApplyPendingWatchResyncSnapshot(Level level)
    {
        if (pendingWatchResyncSnapshot is not { } snapshot
            || (watchTargetRestartPending && !watchTargetRestartSuspended)
            || PlayerLocation.FetchFrom(level.Session).Map != watchMap)
            return;

        ApplyWatchSnapshot(level, snapshot, true);
        Logger.Info(
            LT.MiaoNetWatch,
            $"Applied watch resync snapshot for session {watchSessionID}; " +
            $"room={snapshot.Location.Room}, entities={snapshot.EntityStates.Count}, " +
            $"sequence={snapshot.Sequence}."
        );
    }

    private void Context_WatchEnded(PacketWatchEnded packet)
    {
        if (watchSessionID != packet.SessionID)
            return;

        Logger.Info(LT.MiaoNetWatch, $"Watch session {packet.SessionID} ended by server: {packet.Reason}.");
        OnlinePlayer? player = StopWatching(false);
        string playerName = player?.Info.Name ?? packet.SessionID.ToString(CultureInfo.InvariantCulture);
        string reason = Dialog.Get($"miaonet_commands_watch_ended_{packet.Reason}");
        context.ChatComponent.AddLocalChat(MiaoNetChatText.CreateCommandTip(
            PFormat.Format(
                CultureInfo.CurrentCulture,
                Dialog.Get("miaonet_commands_watch_ended"),
                playerName,
                reason
            )
        ));
    }

    private static void ReplaceFlags(ISet<string> destination, IEnumerable<string> source)
    {
        destination.Clear();
        destination.UnionWith(source);
    }

    private void ApplyWatchEntityState(
        Level level,
        bool allowDuringTransition = false
    )
    {
        if (watchEntityStates is null
            || (!allowDuringTransition && level.transition is not null)
            || PlayerLocation.FetchFrom(level.Session) != watchEntityLocation)
            return;

        using IDisposable entityIndexScope = watchRoomEntityIndex.BeginCapture(level);
        if (watchPendingEntityStateMode != WatchEntityStateMode.None)
        {
            bool replace = watchPendingEntityStateMode == WatchEntityStateMode.Replace;
            bool lifecycleReset = replace && watchEntityLifecycleResetPending;
            WatchEntityState[] states = replace
                ? watchEntityStates.Values.ToArray()
                : watchPendingEntityStateKeys.Select(key => watchEntityStates[key]).ToArray();
            WatchEntityApplySummary summary = WatchEntitySyncRegistry.ApplyStates(
                level,
                states,
                replace,
                lifecycleReset
            );
            WatchEntityApplyResult result = summary.Result;
            watchEntityStateApplied |= result.HasFlag(WatchEntityApplyResult.SceneChanged);
            if (summary.RoomReloadRequestedKinds.Count > 0)
            {
                if (lifecycleReset)
                {
                    watchLifecycleIncompleteKinds.UnionWith(summary.RoomReloadRequestedKinds);
                    Logger.Warn(
                        LT.MiaoNetWatch,
                        $"Completed death respawn after incomplete entity reconciliation " +
                        $"in {watchEntityLocation.Room}; kinds=" +
                        $"{string.Join(",", summary.RoomReloadRequestedKinds)}."
                    );
                }
                else
                {
                    Logger.Warn(
                        LT.MiaoNetWatch,
                        $"Applied watch state for room {watchEntityLocation.Room} without " +
                        $"promoting an entity mismatch to a room reload; kinds=" +
                        $"{string.Join(",", summary.RoomReloadRequestedKinds)}."
                    );
                }
            }
            watchPendingEntityStateKeys.Clear();
            watchPendingEntityStateMode = WatchEntityStateMode.None;
            if (replace)
                watchEntityLifecycleResetPending = false;
        }

        ApplyPendingWatchEntityEvents(level);
    }

    private void ApplyPendingWatchEntityEvents(Level level)
    {
        if (watchPendingEntityEvents.Count == 0
            || PlayerLocation.FetchFrom(level.Session) != watchEntityLocation)
            return;

        foreach (WatchEntityEvent entityEvent in watchPendingEntityEvents)
            WatchEntitySyncRegistry.ApplyEvent(level, entityEvent);
        watchPendingEntityEvents.Clear();
    }

    private bool UpdateWatchSceneRestore(Level level)
    {
        if (!watchSceneRestorePending || level.transition is not null)
            return false;

        watchSceneRestorePending = false;
        if (PlayerLocation.FetchFrom(level.Session) != watchSceneRestoreLocation)
        {
            watchSceneRestoreLocation = default;
            return false;
        }

        watchSceneRestoreLocation = default;
        level.Reload();
        Logger.Info(LT.MiaoNetWatch, "Reloaded the current room after restoring local watch state.");
        return true;
    }

    private bool UpdateWatchRoomReload(Level level)
    {
        if (!watchRoomReloadPending
            || level.transition is not null
            || PlayerLocation.FetchFrom(level.Session) != watchRoomReloadLocation)
            return false;

        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;
        if (watchEntityStates is not null)
        {
            watchPendingEntityStateKeys.Clear();
            watchPendingEntityStateKeys.UnionWith(watchEntityStates.Keys);
            watchPendingEntityStateMode = WatchEntityStateMode.Replace;
        }
        level.Reload();
        Logger.Info(LT.MiaoNetWatch, "Reloaded the current room to follow the watched scene lifecycle.");
        return true;
    }

    private static void GotoLevel(Level level, Player player, Vector2 at)
    {
        var session = level.Session;
        var data = session.MapData.GetAt(at);
        session.Level = data.Name;
        session.RespawnPoint = data.Spawns.ClosestTo(at);
        player.Position = session.RespawnPoint.Value;
        level.LoadLevel(Player.IntroTypes.Transition);
    }

    private void BeginWatchRoomTransition(Level level)
    {
        if (watchRoomTransitionPending
            || watchPlaybackPlayerState is null)
            return;

        PlayerLocation targetLocation = watchPlaybackLocation;
        PlayerLocation currentLocation = PlayerLocation.FetchFrom(level.Session);
        if (targetLocation.IsInDebugMap
            || targetLocation.Map != currentLocation.Map
            || targetLocation.Room == currentLocation.Room)
            return;

        Player? player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return;

        Vector2 targetPosition;
        if (watchRoomTransition is { } bufferedTransition
            && bufferedTransition.SourceLocation == currentLocation
            && bufferedTransition.TargetLocation == targetLocation)
            targetPosition = bufferedTransition.PlayerPosition;
        else if (watchCurrentPlayerFrame is { } currentFrame
            && currentFrame.Value.Location == targetLocation
            && watchPlaybackPlayerState is { } playbackState)
            targetPosition = playbackState.Position;
        else
            return;
        LevelData data = level.Session.MapData.Get(targetLocation.Room);
        player.Visible = false;
        player.StateMachine.State = Player.StFrozen;
        watchRoomTransitionPending = true;
        InvalidateBufferedWatchCamera(awaitFreshSample: true);

        WatchRoomTransition? authoritativeTransition = watchRoomTransition is { } transition
            && transition.SourceLocation == currentLocation
            && transition.TargetLocation == targetLocation
            ? transition
            : null;
        if (authoritativeTransition is { } authoritative)
        {
            Vector2 spawn = data.Spawns.ClosestTo(targetPosition);
            level.Session.RespawnPoint = spawn;
            player.Position = authoritative.PlayerPosition;
            player.Speed = Vector2.Zero;
            level.TransitionTo(data, authoritative.Direction);
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Started authoritative vanilla watch room transition " +
                $"{currentLocation.Room} -> {targetLocation.Room}, " +
                $"direction={authoritative.Direction}, player={authoritative.PlayerPosition}."
            );
            return;
        }

        if (TryGetWatchTransitionDirection(level.Bounds, data.Bounds, out Vector2 direction))
        {
            Vector2 spawn = data.Spawns.ClosestTo(targetPosition);
            level.Session.RespawnPoint = spawn;
            // Match the original wip implementation: put the hidden local Player
            // on a valid target-room spawn before starting the vanilla camera
            // transition. Player.TransitionTo is acknowledged immediately while
            // watching, so this entity can never hold the Level coroutine open.
            player.Position = spawn;
            player.Speed = Vector2.Zero;
            level.TransitionTo(data, direction);
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Started vanilla watch room transition {currentLocation.Room} -> " +
                $"{targetLocation.Room}, direction={direction}."
            );
            return;
        }

        // Non-adjacent room changes cannot use Level.TransitionTo. Teleport directly
        // without a ScreenWipe: a room-owned wipe/cutscene must never become the
        // completion signal for the spectator lifecycle.
        try
        {
            Vector2 spawn = data.Spawns.ClosestTo(targetPosition);
            Vector2 roomLocalSpawn = spawn - new Vector2(data.Bounds.Left, data.Bounds.Top);
            level.TeleportTo(player, data.Name, Player.IntroTypes.Transition, roomLocalSpawn);
            CompleteWatchRoomTransition(level, level.Tracker.GetEntity<Player>() ?? player);
            if (!SnapBufferedWatchCamera(level))
                SnapWatchCamera(level, targetPosition);
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Teleported non-adjacent watch room {currentLocation.Room} -> {targetLocation.Room}."
            );
        }
        finally
        {
            // CompleteWatchRoomTransition normally clears this. Keep the fallback
            // failure path recoverable as well.
            watchRoomTransitionPending = false;
        }
    }

    private void CancelWatchRoomTransition(Level? _)
    {
        watchRoomTransitionPending = false;
    }

    private void CompleteWatchRoomTransition(Level level, Player player)
    {
        watchRoomTransitionPending = false;
        watchRoomTransition = null;
        player.Visible = false;
        player.StateMachine.State = Player.StFrozen;
        NormalizeWatchRoomRendering(level);
        ApplyWatchEntityState(level);
        TryCompleteWatchCrossRoomRespawn(level);

        if (playerWatching is not null
            && ghosts.TryGetValue(playerWatching.ID, out MiaoNetGhost? ghost))
            ghost.SetWatchFocus(true);

    }

    private static bool TryGetWatchTransitionDirection(
        Rectangle current,
        Rectangle target,
        out Vector2 direction
    )
    {
        bool verticalOverlap = target.Bottom > current.Top && target.Top < current.Bottom;
        bool horizontalOverlap = target.Right > current.Left && target.Left < current.Right;
        if (verticalOverlap && target.Left == current.Right)
            direction = Vector2.UnitX;
        else if (verticalOverlap && target.Right == current.Left)
            direction = -Vector2.UnitX;
        else if (horizontalOverlap && target.Top == current.Bottom)
            direction = Vector2.UnitY;
        else if (horizontalOverlap && target.Bottom == current.Top)
            direction = -Vector2.UnitY;
        else
            direction = Vector2.Zero;
        return direction != Vector2.Zero;
    }

    private static void SnapWatchCamera(Level level, Vector2 targetPosition)
        => level.Camera.Position = GetWatchCameraTarget(level, targetPosition);

    private static Vector2 GetWatchCameraTarget(Level level, Vector2 targetPosition)
    {
        const int width = Celeste.GameWidth;
        const int height = Celeste.GameHeight;
        Vector2 target = targetPosition - new Vector2(width, height) / 2f;
        target.X = MathHelper.Clamp(
            target.X,
            level.Bounds.Left,
            level.Bounds.Right - width
        );
        target.Y = MathHelper.Clamp(
            target.Y,
            level.Bounds.Top,
            level.Bounds.Bottom - height
        );
        return target;
    }

    private void UpdateWatchCamera(Level level)
    {
        if (WatchedPlayerState is not { } state)
            return;

        if (TryGetBufferedWatchCamera(level, out Vector2 authoritativeTarget))
        {
            level.Camera.Position = authoritativeTarget;
            return;
        }

        if (watchCameraAwaitingFreshSample)
            return;

        Vector2 target = GetWatchCameraTarget(level, state.Position);
        level.Camera.Position = Calc.Approach(
            level.Camera.Position,
            target,
            ((level.Camera.Position - target).Length() * 4f) * Engine.RawDeltaTime
        );
    }

    internal void ApplyWatchCameraAfterLevelUpdate(Level level)
    {
        if (!watchCameraApplyAfterLevelUpdate)
            return;

        watchCameraApplyAfterLevelUpdate = false;
        if (WatchedPlayerState is null || level.transition is not null)
            return;

        UpdateWatchCamera(level);
        WatchLightningAdapter.RefreshRendererEdgesForCamera(level);
        WatchRoomEnvironmentAdapter.ApplyFrame(level);
    }

    private void BufferWatchPlayerFrame(
        OnlinePlayer player,
        PacketPlayerFrame packet,
        PlayerStateDelta delta
    )
    {
        if (!WatchSceneSyncActive
            || watchResyncPending
            || watchTargetRestartPending
            || watchTargetRestartSuspended
            || playerWatching?.ID != player.ID)
            return;

        WatchPlaybackEnqueueResult result = watchPlayerFrameBuffer.Enqueue(
            context.CurrentReceivedPacketTimestamp,
            new(
                packet.PlayerEpoch,
                packet.PlayerSequence,
                player.Location,
                packet.Kind,
                delta,
                packet.KeyframeState?.Clone()
            )
        );
        if (result != WatchPlaybackEnqueueResult.Success)
            BeginWatchResync(lastWatchSequence, $"player playback buffer {result}");
    }

    private void BufferWatchPlayerPause(bool paused)
    {
        if (!WatchSceneSyncActive
            || watchResyncPending
            || watchTargetRestartPending
            || watchTargetRestartSuspended)
            return;

        WatchPlaybackEnqueueResult result = watchPlayerEventBuffer.Enqueue(
            context.CurrentReceivedPacketTimestamp,
            new(
                WatchPlayerPresentationEventKind.PauseChanged,
                0,
                0,
                paused,
                default,
                Vector2.Zero
            )
        );
        if (result != WatchPlaybackEnqueueResult.Success)
            BeginWatchResync(lastWatchSequence, $"player event playback buffer {result}");
    }

    private void BufferWatchPlayerLiveState(PacketPlayerLiveState packet)
    {
        if (!WatchSceneSyncActive
            || watchResyncPending
            || watchTargetRestartPending
            || watchTargetRestartSuspended)
            return;

        WatchPlaybackEnqueueResult result = watchPlayerEventBuffer.Enqueue(
            context.CurrentReceivedPacketTimestamp,
            new(
                WatchPlayerPresentationEventKind.LiveState,
                packet.PlayerEpoch,
                packet.PlayerSequence,
                false,
                packet.Type,
                packet.Vector2
            )
        );
        if (result != WatchPlaybackEnqueueResult.Success)
            BeginWatchResync(lastWatchSequence, $"player event playback buffer {result}");
    }

    private void AdvanceWatchPlayback(Level level)
    {
        if (!WatchSceneSyncActive || playerWatching is not { } player)
            return;

        long playbackTime = System.Diagnostics.Stopwatch.GetTimestamp() - WatchPlaybackDelayTicks;
        while (TryGetNextWatchPlaybackKind(playbackTime, out WatchPlaybackKind kind))
        {
            if (kind == WatchPlaybackKind.PlayerFrame)
            {
                watchPlayerFrameBuffer.TryDequeueDue(
                    playbackTime,
                    out WatchPlaybackEntry<WatchPlayerFrameSample> entry
                );
                ApplyBufferedWatchPlayerFrame(level, player, entry);
            }
            else if (kind == WatchPlaybackKind.SceneDelta)
            {
                watchSceneDeltaBuffer.TryDequeueDue(
                    playbackTime,
                    out WatchPlaybackEntry<WatchSceneDelta> entry
                );
                if (entry.Value.Sequence != lastWatchSequence + 1)
                {
                    BeginWatchResync(entry.Value.Sequence, "playback sequence gap");
                    return;
                }
                ApplyBufferedWatchSceneDelta(level, entry.Value);
            }
            else
            {
                watchPlayerEventBuffer.TryDequeueDue(
                    playbackTime,
                    out WatchPlaybackEntry<WatchPlayerPresentationEvent> entry
                );
                ApplyBufferedWatchPlayerEvent(level, player, entry.Value);
            }

            if (watchResyncPending)
                return;
        }

        InterpolateWatchPlayerFrame(level, player, playbackTime);
    }

    private enum WatchPlaybackKind
    {
        PlayerFrame,
        SceneDelta,
        PlayerEvent,
    }

    private bool TryGetNextWatchPlaybackKind(long playbackTime, out WatchPlaybackKind kind)
    {
        kind = default;
        long earliest = long.MaxValue;
        bool found = false;

        if (watchPlayerFrameBuffer.TryPeek(out WatchPlaybackEntry<WatchPlayerFrameSample> frame)
            && frame.ReceivedAt <= playbackTime
            && frame.Value.PlayerEpoch <= watchPlayerEpoch)
        {
            earliest = frame.ReceivedAt;
            kind = WatchPlaybackKind.PlayerFrame;
            found = true;
        }
        if (watchSceneDeltaBuffer.TryPeek(out WatchPlaybackEntry<WatchSceneDelta> scene)
            && scene.ReceivedAt <= playbackTime
            && scene.ReceivedAt < earliest)
        {
            earliest = scene.ReceivedAt;
            kind = WatchPlaybackKind.SceneDelta;
            found = true;
        }
        if (watchPlayerEventBuffer.TryPeek(
            out WatchPlaybackEntry<WatchPlayerPresentationEvent> playerEvent
        )
            && playerEvent.ReceivedAt <= playbackTime
            && (playerEvent.Value.Kind == WatchPlayerPresentationEventKind.PauseChanged
                || playerEvent.Value.PlayerEpoch <= watchPlayerEpoch)
            && playerEvent.ReceivedAt < earliest)
        {
            kind = WatchPlaybackKind.PlayerEvent;
            found = true;
        }

        return found;
    }

    private void ApplyBufferedWatchPlayerFrame(
        Level level,
        OnlinePlayer player,
        WatchPlaybackEntry<WatchPlayerFrameSample> entry
    )
    {
        if (entry.Value.PlayerEpoch < watchPlayerEpoch
            || entry.Value.PlayerSequence <= watchPlaybackPlayerSequence)
            return;
        if (entry.Value.PlayerEpoch > watchPlayerEpoch)
            return;

        watchCurrentPlayerFrame = entry;
        PlayerStateDelta delta = entry.Value.Delta;
        if (entry.Value.Kind == PlayerFrameKind.Keyframe)
        {
            watchPlaybackPlayerState = entry.Value.KeyframeState!.Clone();
            watchPlaybackAwaitingKeyframe = false;
        }
        else
        {
            if (watchPlaybackAwaitingKeyframe
                || entry.Value.PlayerSequence != PlayerTimelineSequence.Next(watchPlaybackPlayerSequence))
            {
                watchPlaybackAwaitingKeyframe = true;
                watchCurrentPlayerFrame = null;
                return;
            }
            watchPlaybackPlayerState?.ApplyDelta(delta);
        }
        watchPlaybackPlayerSequence = entry.Value.PlayerSequence;

        PlayerLocation localLocation = PlayerLocation.FetchFrom(level.Session);
        if (entry.Value.Location == watchPlaybackLocation
            && entry.Value.Location == localLocation)
        {
            WatchBadelineOldsiteAdapter.RecordRemotePlayerFrame(delta);
            WatchAngryOshiroAdapter.RecordRemotePlayerFrame(delta);
            ApplyPlayerFrame(level, player, delta, delta.Position);
        }
    }

    private void ApplyBufferedWatchPlayerEvent(
        Level level,
        OnlinePlayer player,
        WatchPlayerPresentationEvent playerEvent
    )
    {
        if (playerEvent.Kind == WatchPlayerPresentationEventKind.PauseChanged)
        {
            watchPlaybackPaused = playerEvent.Paused;
            if (ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost))
                ghost.OnUpdatePaused(playerEvent.Paused);
            return;
        }

        if (playerEvent.PlayerEpoch != watchPlayerEpoch
            || playerEvent.PlayerSequence <= watchPlaybackPlayerSequence)
            return;
        if (watchPlaybackAwaitingKeyframe
            || playerEvent.PlayerSequence != PlayerTimelineSequence.Next(watchPlaybackPlayerSequence))
        {
            watchPlaybackAwaitingKeyframe = true;
            return;
        }
        watchPlaybackPlayerSequence = playerEvent.PlayerSequence;

        if (playerEvent.Kind == WatchPlayerPresentationEventKind.TargetRestarting)
        {
            BeginWatchTargetRestart(level, playerEvent.RestartKind);
            return;
        }

        ApplyPlayerLiveState(level, player, playerEvent.LiveState, playerEvent.Value);
    }

    private void Context_WatchTargetRestarting(
        PacketWatchTargetRestartingNotification packet
    )
    {
        if (watchSessionID != packet.SessionID
            || playerWatching?.ID != packet.TargetPlayerID
            || watchTargetRestartPending
            || watchTargetRestartSuspended)
            return;

        watchTargetRestartPending = true;
        WatchPlaybackEnqueueResult result = watchPlayerEventBuffer.Enqueue(
            context.CurrentReceivedPacketTimestamp,
            new WatchPlayerPresentationEvent(
                WatchPlayerPresentationEventKind.TargetRestarting,
                packet.PlayerEpoch,
                packet.PlayerSequence,
                false,
                default,
                Vector2.Zero,
                packet.Kind
            )
        );
        if (result == WatchPlaybackEnqueueResult.Success)
            return;

        Logger.Warn(
            LT.MiaoNetWatch,
            $"Could not buffer watched target restart ({result}); applying it immediately."
        );
        if (Engine.Scene is Level level)
            BeginWatchTargetRestart(level, packet.Kind);
    }

    private void BeginWatchTargetRestart(Level level, WatchTargetRestartKind kind)
    {
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.None)
            BeginWatchDeathTransition(level);
        SignalWatchDeathWipe(level);

        watchTargetRestartPending = false;
        watchTargetRestartSuspended = true;
        watchTargetRestartKind = kind;
        watchResyncPending = true;
        watchPlayerFrameBuffer.Clear();
        watchSceneDeltaBuffer.Clear();
        watchPlayerEventBuffer.Clear();
        watchCurrentPlayerFrame = null;
        Logger.Info(LT.MiaoNetWatch, $"Holding watch session through target restart {kind}.");
    }

    private void InterpolateWatchPlayerFrame(
        Level level,
        OnlinePlayer player,
        long playbackTime
    )
    {
        if (watchCurrentPlayerFrame is not { } current)
            return;

        Vector2 position = current.Value.Delta.Position;
        Vector2? camera = current.Value.Delta.HasCameraPosition
            ? current.Value.Delta.CameraPosition
            : null;
        bool hasNextFrame = watchPlayerFrameBuffer.TryPeek(
            out WatchPlaybackEntry<WatchPlayerFrameSample> next
        ) && next.Value.PlayerEpoch == current.Value.PlayerEpoch
            && next.Value.Location == current.Value.Location;
        if (hasNextFrame)
        {
            float amount = WatchPlaybackTiming.GetInterpolationAmount(
                current.ReceivedAt,
                next.ReceivedAt,
                playbackTime
            );
            position = Vector2.Lerp(position, next.Value.Delta.Position, amount);
            if (camera.HasValue && next.Value.Delta.HasCameraPosition)
                camera = Vector2.Lerp(camera.Value, next.Value.Delta.CameraPosition, amount);
        }

        if (watchPlaybackPlayerState is not null)
            watchPlaybackPlayerState.Position = position;
        if (ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost)
            && !ghost.BeingHeldLocally
            && current.Value.Location == watchPlaybackLocation)
            ghost.Position = position;
        if (camera.HasValue && current.Value.Location == watchPlaybackLocation)
        {
            watchCameraLocation = current.Value.Location;
            watchCameraTarget = camera.Value;
            watchCameraAwaitingFreshSample = false;
        }
    }

    private void ResetWatchPlayerPlayback(
        OnlinePlayer player,
        PlayerLocation location,
        uint playerEpoch,
        uint playerSequenceWatermark
    )
    {
        watchPlayerFrameBuffer.Clear();
        watchPlayerEventBuffer.Clear();
        watchCurrentPlayerFrame = null;
        watchPlaybackPlayerState = player.State?.Clone();
        watchPlayerEpoch = playerEpoch;
        watchPlaybackPlayerSequence = Math.Max(
            playerSequenceWatermark,
            player.PlayerEpoch == playerEpoch ? player.LastPlayerSequence : 0
        );
        watchPlaybackAwaitingKeyframe = false;
        watchPlaybackLocation = location;
        watchPlaybackPaused = player.IsPaused;
        if (ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost))
            ghost.OnUpdatePaused(watchPlaybackPaused);
    }

    private void ClearWatchPlayerPlayback()
    {
        watchPlayerFrameBuffer.Clear();
        watchPlayerEventBuffer.Clear();
        watchCurrentPlayerFrame = null;
        watchPlaybackPlayerState = null;
        watchPlayerEpoch = 0;
        watchPlaybackPlayerSequence = 0;
        watchPlaybackAwaitingKeyframe = false;
        watchPlaybackLocation = default;
        watchPlaybackPaused = false;
    }

    private bool TryGetBufferedWatchCamera(Level level, out Vector2 target)
    {
        target = default;
        if (watchCameraTarget is not { } cameraTarget
            || watchCameraLocation != PlayerLocation.FetchFrom(level.Session))
            return false;

        target = cameraTarget;
        return true;
    }

    private bool SnapBufferedWatchCamera(Level level)
    {
        if (!TryGetBufferedWatchCamera(level, out Vector2 target))
            return false;

        level.Camera.Position = target;
        return true;
    }

    private void InvalidateBufferedWatchCamera(bool awaitFreshSample = false)
    {
        watchCameraLocation = default;
        watchCameraTarget = null;
        watchCameraAwaitingFreshSample = awaitFreshSample;
    }

    private static void NormalizeWatchRoomRendering(Level level)
    {
        level.Lighting.Alpha = level.DarkRoom
            ? level.Session.DarkRoomAlpha
            : level.BaseLightingAlpha + level.Session.LightingAlphaAdd;
        level.Bloom.Base = AreaData.Get(level.Session).BloomBase + level.Session.BloomBaseAdd;
        level.SnapColorGrade(level.Session.ColorGrade);
    }
}
