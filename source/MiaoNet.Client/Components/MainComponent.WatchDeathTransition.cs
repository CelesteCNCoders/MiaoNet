using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private enum WatchDeathTransitionPhase
    {
        None,
        WaitingForRespawnState,
        WipingOut,
    }

    private WatchDeathTransitionPhase watchDeathTransitionPhase;
    private bool watchDeathWipeSignaled;
    private bool watchDeathRespawnStateReady;
    private bool watchDeathRespawnNotificationReady;
    private Vector2 watchDeathRespawnPosition;
    private bool watchDeathRespawnFromSaveState;
    private PlayerLocation watchDeathSourceLocation;
    private PlayerLocation watchDeathRespawnLocation;
    private ScreenWipe? watchDeathWipe;
    private float watchDeathFreshCameraWait;

    private void BeginWatchDeathTransition(Level level)
    {
        CancelWatchDeathTransition(level);
        InvalidateBufferedWatchCamera(awaitFreshSample: true);
        watchDeathTransitionPhase = WatchDeathTransitionPhase.WaitingForRespawnState;
        watchDeathSourceLocation = PlayerLocation.FetchFrom(level.Session);
        Logger.Debug(LT.MiaoNetWatch, "Started Watcher visual death lifecycle.");
    }

    private void SignalWatchDeathWipe(Level level)
    {
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.None)
            BeginWatchDeathTransition(level);

        watchDeathWipeSignaled = true;
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.WaitingForRespawnState
            && (level.Wipe is null || level.Wipe.Completed))
            StartWatchDeathWipeOut(level);
    }

    private void BufferWatchRespawnNotification(Vector2 position, bool fromSaveState)
    {
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.None)
            return;

        watchDeathRespawnPosition = position;
        watchDeathRespawnFromSaveState = fromSaveState;
        watchDeathRespawnNotificationReady = true;
    }

    private void MarkWatchDeathRespawnStateReady(PlayerLocation location)
    {
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.None
            || playerWatching is null
            || playerWatching.Location.Map != location.Map)
            return;

        watchDeathRespawnLocation = location;
        watchDeathRespawnStateReady = true;
    }

    private bool UpdateWatchDeathTransition(Level level)
    {
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.None)
            return false;

        PlayerLocation current = PlayerLocation.FetchFrom(level.Session);
        if (playerWatching is null || playerWatching.Location.Map != current.Map)
        {
            CancelWatchDeathTransition(level);
            return false;
        }
        if (playerWatching.Location != current)
            // A PlayerSeeker Void ending is a death followed by a direct room
            // load. Preserve the death state while the normal watch room
            // transition catches up instead of discarding its respawn packet.
            return false;

        if (TryCompleteWatchCrossRoomRespawn(level))
            return false;

        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.WaitingForRespawnState
            && watchDeathWipeSignaled
            && (level.Wipe is null || level.Wipe.Completed))
            StartWatchDeathWipeOut(level);

        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.WipingOut
            && watchDeathWipe is { Completed: false, Percent: >= 1f } wipe)
        {
            if (watchDeathRespawnStateReady && watchDeathRespawnNotificationReady
                && watchCameraAwaitingFreshSample)
                watchDeathFreshCameraWait += Engine.RawDeltaTime;

            bool cameraReady = !watchCameraAwaitingFreshSample
                || watchDeathFreshCameraWait >= 0.75f;
            if (watchDeathRespawnStateReady && watchDeathRespawnNotificationReady && cameraReady)
                wipe.EndTimer = 0f;
            else
                // ScreenWipe deliberately spends one update at Percent=1 before
                // completing. Refresh EndTimer during that fully black frame so
                // a delayed respawn snapshot cannot expose the stale room.
                wipe.EndTimer = Math.Max(wipe.EndTimer, 0.05f);
        }

        // Before the screen is fully black, keep receiving state into the
        // pending caches but do not expose the new lifecycle in the live scene.
        return true;
    }

    private void StartWatchDeathWipeOut(Level level)
    {
        watchDeathTransitionPhase = WatchDeathTransitionPhase.WipingOut;
        level.DoScreenWipe(false, () => CompleteWatchDeathWipeOut(level), false);
        watchDeathWipe = level.Wipe;
        Logger.Debug(LT.MiaoNetWatch, "Started Watcher death wipe at the Player wipe event.");
    }

    private void CompleteWatchDeathWipeOut(Level level)
    {
        if (watchDeathTransitionPhase != WatchDeathTransitionPhase.WipingOut
            || Engine.Scene != level
            || playerWatching is null
            || !watchDeathRespawnStateReady
            || !watchDeathRespawnNotificationReady)
            return;

        ApplyWatchDeathRespawnSceneState(level);
        if (!SnapBufferedWatchCamera(level))
            SnapWatchCamera(level, watchDeathRespawnPosition);
        if (ghosts.TryGetValue(playerWatching.ID, out MiaoNetGhost? ghost))
        {
            ghost.OnRespawning(
                watchDeathRespawnPosition,
                watchDeathRespawnFromSaveState
            );
        }

        ResetWatchDeathTransitionState();
        Logger.Debug(
            LT.MiaoNetWatch,
            "Applied post-respawn scene state and camera at the fully black frame before reveal."
        );
    }

    private bool TryCompleteWatchCrossRoomRespawn(Level level)
    {
        PlayerLocation current = PlayerLocation.FetchFrom(level.Session);
        if (!watchDeathRespawnStateReady
            || !watchDeathRespawnNotificationReady
            || watchDeathRespawnLocation != current
            || watchDeathSourceLocation == current)
            return false;

        ApplyWatchDeathRespawnSceneState(level);
        if (!SnapBufferedWatchCamera(level))
            SnapWatchCamera(level, watchDeathRespawnPosition);
        if (playerWatching is not null
            && ghosts.TryGetValue(playerWatching.ID, out MiaoNetGhost? ghost)
            && ghost.Dead)
        {
            // The target room is already authoritative. Restore the Ghost
            // immediately so no same-room death tween flashes after the Void
            // room has disappeared.
            ghost.OnRespawning(watchDeathRespawnPosition, fromSL: true);
        }

        string sourceRoom = watchDeathSourceLocation.Room;
        CancelWatchDeathTransition(level);
        Logger.Debug(
            LT.MiaoNetWatch,
            $"Completed cross-room watched death lifecycle " +
            $"{sourceRoom} -> {current.Room}."
        );
        return true;
    }

    private void ApplyWatchDeathRespawnSceneState(Level level)
    {
        ApplyWatchTouchSwitchState(level, allowDuringTransition: true);
        ApplyWatchEntityState(level, allowDuringTransition: true);
        if (watchLifecycleTouchSwitchRepairIncomplete || watchLifecycleIncompleteKinds.Count > 0)
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Completed lightweight watched death respawn with localized reconciliation " +
                $"gaps in {watchEntityLocation.Room}; touchSwitches=" +
                $"{watchLifecycleTouchSwitchRepairIncomplete}, kinds=" +
                $"{string.Join(",", watchLifecycleIncompleteKinds.Order())}."
            );

        // Only a producer delta carrying RequiresRoomReload may authorize
        // UpdateWatchRoomReload to call Level.Reload. Adapter mismatches during
        // death remain localized and can never promote a lightweight respawn.
        watchLifecycleIncompleteKinds.Clear();
        watchLifecycleTouchSwitchRepairIncomplete = false;
    }

    private void CancelWatchDeathTransition(Level? level)
    {
        if (watchDeathWipe is { Completed: false } wipe
            && (level is null || ReferenceEquals(wipe.Scene, level)))
            wipe.Cancel();

        ResetWatchDeathTransitionState();
    }

    private void ResetWatchDeathTransitionState()
    {
        watchDeathTransitionPhase = WatchDeathTransitionPhase.None;
        watchDeathWipeSignaled = false;
        watchDeathRespawnStateReady = false;
        watchDeathRespawnNotificationReady = false;
        watchDeathRespawnPosition = Vector2.Zero;
        watchDeathRespawnFromSaveState = false;
        watchDeathSourceLocation = default;
        watchDeathRespawnLocation = default;
        watchDeathWipe = null;
        watchDeathFreshCameraWait = 0f;
    }
}
