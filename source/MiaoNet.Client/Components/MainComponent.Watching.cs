using System.Globalization;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private int? watchSessionID;
    private HashSet<string>? watchBaselineFlags;
    private PlayerMapLocation watchMap;
    private int lastWatchSequence;
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
            if (playerWatching.State is null)
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
            PlayerLocation watchedPlayerLocation = playerWatching.Location;
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
        ReplaceFlags(level.Session.Flags, snapshot.Flags);
        watchMap = snapshot.Location.Map;
        lastWatchSequence = snapshot.Sequence;
        watchEntityLocation = snapshot.Location;
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
        watchResyncPending = false;
        pendingWatchResyncSnapshot = null;
        watchMap = default;
        watchEntityLocation = default;
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

        return player;
    }

    private void Context_WatchSceneDeltaReceived(PacketWatchSceneDeltaNotification packet)
    {
        if (watchSessionID != packet.SessionID
            || playerWatching?.ID != packet.TargetPlayerID)
            return;

        if (packet.Delta.Sequence <= lastWatchSequence || watchResyncPending)
            return;

        if (packet.Delta.Sequence != lastWatchSequence + 1)
        {
            BeginWatchResync(packet.Delta.Sequence, "sequence gap");
            return;
        }

        if (Engine.Scene is not Level level
            || PlayerLocation.FetchFrom(level.Session).Map != watchMap
            || packet.Delta.Location.Map != watchMap
            || (packet.Delta.EntityStateMode == WatchEntityStateMode.Patch
                && watchEntityLocation != packet.Delta.Location))
        {
            BeginWatchResync(packet.Delta.Sequence, "scene mismatch");
            return;
        }

        packet.Delta.ApplyTo(level.Session.Flags);
        if (watchRoomReloadPending && watchRoomReloadLocation != packet.Delta.Location)
        {
            watchRoomReloadPending = false;
            watchRoomReloadLocation = default;
        }
        if (packet.Delta.EntityStateMode == WatchEntityStateMode.Replace)
        {
            watchEntityLocation = packet.Delta.Location;
            watchEntityStates ??= new();
            watchEntityStates.Clear();
            foreach (WatchEntityState state in packet.Delta.EntityStates)
                watchEntityStates.Add(state.Key, state);
            watchPendingEntityStateKeys.Clear();
            watchPendingEntityStateKeys.UnionWith(watchEntityStates.Keys);
            // A death-respawn Replace arrives after the event that caused the
            // death. Consume that event against the still-live room before the
            // black-frame snapshot replaces the lifecycle; otherwise a
            // Snowball impact can be discarded without ever being rendered.
            if (packet.Delta.IsDeathRespawn)
            {
                if (IsWatchDeathRoomUnloaded)
                    watchPendingEntityEvents.Clear();
                else
                    ApplyPendingWatchEntityEvents(level);
            }
            else
                watchPendingEntityEvents.Clear();
            watchPendingEntityStateMode = WatchEntityStateMode.Replace;
            watchEntityLifecycleResetPending = packet.Delta.IsDeathRespawn;
            if (packet.Delta.IsDeathRespawn)
                watchLifecycleIncompleteKinds.Clear();
            watchRoomTransition = packet.Delta.RoomTransition;
        }
        else if (packet.Delta.EntityStateMode == WatchEntityStateMode.Patch)
        {
            watchEntityStates ??= new();
            foreach (WatchEntityState state in packet.Delta.EntityStates)
            {
                watchEntityStates[state.Key] = state;
                watchPendingEntityStateKeys.Add(state.Key);
            }
            if (watchPendingEntityStateMode == WatchEntityStateMode.None)
                watchPendingEntityStateMode = WatchEntityStateMode.Patch;
        }
        watchPendingEntityEvents.AddRange(packet.Delta.EntityEvents);
        if (WatchSceneLifecyclePolicy.AuthorizesRoomReload(packet.Delta))
        {
            watchRoomReloadPending = true;
            watchRoomReloadLocation = packet.Delta.Location;
        }
        if (packet.Delta.IsDeathRespawn)
            MarkWatchDeathRespawnStateReady(packet.Delta.Location);
        lastWatchSequence = packet.Delta.Sequence;
    }

    private void BeginWatchResync(int receivedSequence, string reason)
    {
        if (watchSessionID is not int sessionID || watchResyncPending)
            return;

        watchResyncPending = true;
        pendingWatchResyncSnapshot = null;
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

        watchResyncPending = true;
        pendingWatchResyncSnapshot = packet.Snapshot;
        if (Engine.Scene is Level level)
            ApplyPendingWatchResyncSnapshot(level);
    }

    private void ApplyPendingWatchResyncSnapshot(Level level)
    {
        if (pendingWatchResyncSnapshot is not { } snapshot
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
            || playerWatching?.State is null)
            return;

        PlayerLocation targetLocation = playerWatching.Location;
        PlayerLocation currentLocation = PlayerLocation.FetchFrom(level.Session);
        if (targetLocation.IsInDebugMap
            || targetLocation.Map != currentLocation.Map
            || targetLocation.Room == currentLocation.Room)
            return;

        Player? player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return;

        Vector2 targetPosition = playerWatching.State.Position;
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
        if (playerWatching?.State is not { } state)
            return;

        if (TryGetBufferedWatchCamera(level, out Vector2 authoritativeTarget))
        {
            const float CameraResponse = 30f;
            float amount = 1f - MathF.Exp(-CameraResponse * Engine.RawDeltaTime);
            level.Camera.Position = Vector2.Lerp(
                level.Camera.Position,
                authoritativeTarget,
                amount
            );
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
        if (playerWatching?.State is null || level.transition is not null)
            return;

        UpdateWatchCamera(level);
        WatchLightningAdapter.RefreshRendererEdgesForCamera(level);
        WatchRoomEnvironmentAdapter.ApplyFrame(level);
    }

    private void BufferWatchCameraSample(OnlinePlayer player, PlayerStateDelta delta)
    {
        if (!WatchSceneSyncActive
            || playerWatching?.ID != player.ID
            || !delta.HasCameraPosition)
            return;

        watchCameraLocation = player.Location;
        watchCameraTarget = delta.CameraPosition;
        watchCameraAwaitingFreshSample = false;
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
