using System.Globalization;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private int? watchSessionID;
    private HashSet<string>? watchBaselineFlags;
    private PlayerMapLocation watchMap;
    private int lastWatchSequence;
    private PlayerLocation watchTouchSwitchLocation;
    private HashSet<int>? watchActiveTouchSwitchIDs;
    private bool watchTouchSwitchStatePending;
    private bool watchTouchSwitchStateApplied;
    private PlayerLocation watchEntityLocation;
    private Dictionary<WatchEntityKey, WatchEntityState>? watchEntityStates;
    private readonly HashSet<WatchEntityKey> watchPendingEntityStateKeys = new();
    private readonly List<WatchEntityEvent> watchPendingEntityEvents = new();
    private WatchEntityStateMode watchPendingEntityStateMode;
    private bool watchEntityLifecycleResetPending;
    private bool watchLifecycleRoomReloadRequired;
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

    public bool WatchRequestPending { get; private set; }

    private void CleanUpWatching()
    {
        WatchRequestPending = false;
        StopWatching(false);
        ClearWatchSceneProducer();
    }

    private void UpdateWatching(Level level, Player player)
    {
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

            player.Visible = false;
            player.StateMachine.State = Player.StFrozen;
            if (watchRoomTransitionPending)
            {
                if (level.transition is not null)
                {
                    // Level.TransitionTo owns the camera until its coroutine finishes.
                    // The target room is loaded near the beginning of that coroutine,
                    // so reconcile its scene while the camera is still moving into it.
                    ApplyWatchTouchSwitchState(level, allowDuringTransition: true);
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

            PlayerLocation selfLoc = PlayerLocation.FetchFrom(level.Session);
            var otherLoc = playerWatching.Location;
            if (selfLoc.Room != otherLoc.Room && !otherLoc.IsInDebugMap && level.transition is null)
            {
                // Wait for the ordered room Replace. Player location packets can
                // arrive before the scene delta carrying the authoritative vanilla
                // transition direction and target-room entity snapshot.
                if (watchEntityLocation != otherLoc
                    || watchPendingEntityStateMode != WatchEntityStateMode.Replace)
                    return;
                BeginWatchRoomTransition(level);
                return;
            }

            if (UpdateWatchRoomReload(level))
                return;

            ApplyWatchTouchSwitchState(level);
            ApplyWatchEntityState(level);
            player.Visible = false;
            player.StateMachine.State = Player.StFrozen;

            if (level.InCutscene && !level.SkippingCutscene)
                level.SkipCutscene();

            UpdateWatchCamera(level);
        }
    }

    public bool TryBeginWatchRequest()
    {
        if (WatchRequestPending || Watching || watchSceneRestorePending)
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

    public bool StartWatching(OnlinePlayer player, int sessionID, WatchSceneSnapshot snapshot)
    {
        if (Engine.Scene is not Level level
            || snapshot.Location.Map != ClientState.Self.Location.Map
            || snapshot.Location.Map != player.Location.Map)
            return false;

        watchBaselineFlags = new(level.Session.Flags, StringComparer.Ordinal);
        watchPersistentSessionBaseline = WatchPersistentSessionBaseline.Capture(level.Session);
        ReplaceFlags(level.Session.Flags, snapshot.Flags);
        watchMap = snapshot.Location.Map;
        lastWatchSequence = snapshot.Sequence;
        watchSessionID = sessionID;
        playerWatching = player;
        WatchBadelineOldsiteAdapter.ResetRemotePlayerHistory();
        WatchAngryOshiroAdapter.ResetRemotePlayerState();
        WatchPlayerSeekerAdapter.ResetRemoteState();
        if (ghosts.TryGetValue(player.ID, out MiaoNetGhost? ghost))
            ghost.SetWatchFocus(true);
        watchTouchSwitchLocation = snapshot.Location;
        watchActiveTouchSwitchIDs = snapshot.ActiveTouchSwitchIDs.ToHashSet();
        watchTouchSwitchStatePending = true;
        watchTouchSwitchStateApplied = false;
        watchEntityLocation = snapshot.Location;
        watchEntityStates = snapshot.EntityStates.ToDictionary(state => state.Key);
        watchPendingEntityStateKeys.Clear();
        watchPendingEntityStateKeys.UnionWith(watchEntityStates.Keys);
        watchPendingEntityEvents.Clear();
        watchPendingEntityStateMode = WatchEntityStateMode.Replace;
        watchEntityLifecycleResetPending = false;
        watchLifecycleRoomReloadRequired = false;
        watchEntityStateApplied = false;
        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;
        watchRoomTransitionPending = false;
        watchRoomTransition = null;
        // PlayerState can retain the last optional camera sample from an older
        // watch session. Wait for the first frame produced for this session so
        // a stale sample cannot pull the Watcher before the fresh frame arrives.
        watchCameraLocation = default;
        watchCameraTarget = null;
        watchCameraAwaitingFreshSample = false;
        CancelWatchDeathTransition(level);

        Logger.Info(
            LT.MiaoNetWatch,
            $"Watch session {sessionID} started for player {player.ID}; " +
            $"snapshot flags={snapshot.Flags.Count}, " +
            $"touchSwitches={snapshot.ActiveTouchSwitchIDs.Count}, " +
            $"entities={snapshot.EntityStates.Count}, sequence={snapshot.Sequence}."
        );
        return true;
    }

    public OnlinePlayer? StopWatching(bool notifyServer = true)
    {
        OnlinePlayer? player = playerWatching;
        var level = Engine.Scene as Level ?? (Engine.Scene as AssetReloadHelper)?.OrigScene as Level;
        CancelWatchRoomTransition(level);
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

        bool restoreScene = watchTouchSwitchStateApplied || watchEntityStateApplied;
        PlayerLocation restoreLocation = watchEntityStateApplied
            ? watchEntityLocation
            : watchTouchSwitchLocation;

        watchSessionID = null;
        lastWatchSequence = 0;
        watchMap = default;
        watchTouchSwitchLocation = default;
        watchActiveTouchSwitchIDs = null;
        watchTouchSwitchStatePending = false;
        watchTouchSwitchStateApplied = false;
        watchEntityLocation = default;
        watchEntityStates = null;
        watchPendingEntityStateKeys.Clear();
        watchPendingEntityEvents.Clear();
        watchPendingEntityStateMode = WatchEntityStateMode.None;
        watchEntityLifecycleResetPending = false;
        watchLifecycleRoomReloadRequired = false;
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
            || playerWatching?.ID != packet.TargetPlayerID
            || packet.Delta.Sequence != lastWatchSequence + 1
            || Engine.Scene is not Level level
            || PlayerLocation.FetchFrom(level.Session).Map != watchMap
            || packet.Delta.Location.Map != watchMap
            || (packet.Delta.EntityStateMode == WatchEntityStateMode.Patch
                && watchEntityLocation != packet.Delta.Location))
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Rejected watch delta for session {packet.SessionID}, target {packet.TargetPlayerID}, " +
                $"sequence {packet.Delta.Sequence}."
            );
            return;
        }

        packet.Delta.ApplyTo(level.Session.Flags);
        if (watchRoomReloadPending && watchRoomReloadLocation != packet.Delta.Location)
        {
            watchRoomReloadPending = false;
            watchRoomReloadLocation = default;
        }
        if (packet.Delta.HasTouchSwitchState)
        {
            if (watchTouchSwitchLocation != packet.Delta.Location)
                watchTouchSwitchStateApplied = false;
            watchTouchSwitchLocation = packet.Delta.Location;
            watchActiveTouchSwitchIDs = packet.Delta.ActiveTouchSwitchIDs.ToHashSet();
            watchTouchSwitchStatePending = true;
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
                ApplyPendingWatchEntityEvents(level);
            else
                watchPendingEntityEvents.Clear();
            watchPendingEntityStateMode = WatchEntityStateMode.Replace;
            watchEntityLifecycleResetPending = packet.Delta.IsDeathRespawn;
            if (packet.Delta.IsDeathRespawn)
                watchLifecycleRoomReloadRequired = false;
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
        if (packet.Delta.RequiresRoomReload)
        {
            watchRoomReloadPending = true;
            watchRoomReloadLocation = packet.Delta.Location;
        }
        if (packet.Delta.IsDeathRespawn)
            MarkWatchDeathRespawnStateReady(packet.Delta.Location);
        lastWatchSequence = packet.Delta.Sequence;
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

    private void ApplyWatchTouchSwitchState(
        Level level,
        bool allowDuringTransition = false
    )
    {
        if (!watchTouchSwitchStatePending
            || watchActiveTouchSwitchIDs is null
            || (!allowDuringTransition && level.transition is not null)
            || PlayerLocation.FetchFrom(level.Session) != watchTouchSwitchLocation)
            return;

        HashSet<int> missingIDs = new(watchActiveTouchSwitchIDs);
        int activatedCount = 0;
        int resetCount = 0;
        bool lifecycleReset = watchEntityLifecycleResetPending
            && watchEntityLocation == watchTouchSwitchLocation;
        bool suppressAudio = !MiaoNetModule.Settings.PlayerAudioSyncMode.HasReceive;
        IDisposable? audioSuppression = suppressAudio ? WatchSceneAudioSuppression.Begin() : null;
        try
        {
            if (lifecycleReset)
            {
                TouchSwitch[] current = level.Tracker.GetEntities<TouchSwitch>()
                    .Cast<TouchSwitch>()
                    .ToArray();
                bool hasStaleActivation = current.Any(touchSwitch =>
                    touchSwitch.Switch.Activated
                    && TouchSwitchIDTracker.TryGetID(touchSwitch, level.Session.Level, out int id)
                    && !watchActiveTouchSwitchIDs.Contains(id)
                );
                HashSet<int> mapIDs = level.Session.LevelData.Entities
                    .Where(data => data.Name == "touchSwitch")
                    .Select(data => data.ID)
                    .ToHashSet();
                HashSet<int> currentIDs = current
                    .Select(touchSwitch => TouchSwitchIDTracker.TryGetID(
                        touchSwitch,
                        level.Session.Level,
                        out int id
                    ) ? id : -1)
                    .Where(id => id >= 0)
                    .ToHashSet();
                bool missingMapInstance = !mapIDs.SetEquals(currentIDs);
                if (hasStaleActivation || missingMapInstance)
                {
                    if (TryRecreateWatchTouchSwitches(level, current, mapIDs.Count, out resetCount))
                        watchTouchSwitchStateApplied = true;
                    else
                        watchLifecycleRoomReloadRequired = true;
                }
            }

            foreach (TouchSwitch touchSwitch in level.Tracker.GetEntities<TouchSwitch>().Cast<TouchSwitch>())
            {
                if (!TouchSwitchIDTracker.TryGetID(touchSwitch, level.Session.Level, out int id)
                    || !watchActiveTouchSwitchIDs.Contains(id))
                    continue;

                missingIDs.Remove(id);
                if (!touchSwitch.Switch.Activated)
                {
                    touchSwitch.TurnOn();
                    activatedCount++;
                }
            }
        }
        finally
        {
            audioSuppression?.Dispose();
        }

        watchTouchSwitchStatePending = false;
        watchTouchSwitchStateApplied |= activatedCount > 0;
        if (missingIDs.Count > 0)
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Could not find {missingIDs.Count} TouchSwitch instance(s) while applying " +
                $"watch state for room {watchTouchSwitchLocation.Room}."
            );
        }
        Logger.Debug(
            LT.MiaoNetWatch,
            $"Applied TouchSwitch watch state for room {watchTouchSwitchLocation.Room}; " +
            $"requested={watchActiveTouchSwitchIDs.Count}, activated={activatedCount}, " +
            $"lifecycleReset={resetCount}, " +
            $"audioSuppressed={suppressAudio}."
        );
    }

    private static bool TryRecreateWatchTouchSwitches(
        Level level,
        IReadOnlyCollection<TouchSwitch> current,
        int expectedCount,
        out int recreatedCount
    )
    {
        recreatedCount = 0;
        LevelData levelData = level.Session.LevelData;
        EntityData[] data = levelData.Entities
            .Where(entity => entity.Name == "touchSwitch")
            .ToArray();
        if (data.Length != expectedCount)
            return false;

        foreach (TouchSwitch touchSwitch in current)
        {
            touchSwitch.Visible = false;
            touchSwitch.RemoveSelf();
        }
        level.Entities.UpdateLists();

        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        foreach (EntityData entityData in data)
        {
            level.Add(new TouchSwitch(entityData, offset));
            recreatedCount++;
        }
        level.Entities.UpdateLists();
        return recreatedCount == expectedCount;
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
            WatchEntityApplyResult result = WatchEntitySyncRegistry.ApplyStates(
                level,
                states,
                replace,
                lifecycleReset
            );
            watchEntityStateApplied |= result.HasFlag(WatchEntityApplyResult.SceneChanged);
            if (result.HasFlag(WatchEntityApplyResult.RequiresRoomReload))
            {
                if (lifecycleReset)
                {
                    // The death wipe owns a fully black frame. Defer the single
                    // correctness fallback to that boundary instead of exposing a
                    // stale one-way entity or reloading during ordinary watching.
                    watchLifecycleRoomReloadRequired = true;
                }
                else
                {
                    Logger.Warn(
                        LT.MiaoNetWatch,
                        $"Applied watch state for room {watchEntityLocation.Room} without " +
                        "promoting an entity mismatch outside a death lifecycle."
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
        watchTouchSwitchStateApplied = true;
        watchTouchSwitchStatePending = watchActiveTouchSwitchIDs is not null;
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
        ApplyWatchTouchSwitchState(level);
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
    {
        const int W = Celeste.GameWidth;
        const int H = Celeste.GameHeight;
        Vector2 cameraTarget = targetPosition - new Vector2(W, H) / 2f;
        cameraTarget.X = MathHelper.Clamp(
            cameraTarget.X,
            level.Bounds.Left,
            level.Bounds.Right - W
        );
        cameraTarget.Y = MathHelper.Clamp(
            cameraTarget.Y,
            level.Bounds.Top,
            level.Bounds.Bottom - H
        );
        level.Camera.Position = cameraTarget;
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

        const int W = Celeste.GameWidth;
        const int H = Celeste.GameHeight;
        Vector2 target = state.Position - new Vector2(W, H) / 2f;
        target.X = MathHelper.Clamp(target.X, level.Bounds.Left, level.Bounds.Right - W);
        target.Y = MathHelper.Clamp(target.Y, level.Bounds.Top, level.Bounds.Bottom - H);
        level.Camera.Position = Calc.Approach(
            level.Camera.Position,
            target,
            ((level.Camera.Position - target).Length() * 4f) * Engine.RawDeltaTime
        );
    }

    private void BufferWatchCameraSample(OnlinePlayer player, PlayerStateDelta delta)
    {
        if (playerWatching?.ID != player.ID || !delta.HasCameraPosition)
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
