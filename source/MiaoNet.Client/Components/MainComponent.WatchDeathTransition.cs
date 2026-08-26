using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private enum WatchDeathTransitionPhase
    {
        None,
        WaitingForRespawnState,
        WipingOut,
        WaitingForRespawnLoad,
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
    private bool watchDeathRoomUnloaded;

    private bool IsWatchDeathRoomUnloaded
        => watchDeathTransitionPhase == WatchDeathTransitionPhase.WaitingForRespawnLoad
            && watchDeathRoomUnloaded;

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
            || watchPlaybackLocation.Map != location.Map)
            return;

        watchDeathRespawnLocation = location;
        watchDeathRespawnStateReady = true;
    }

    private bool UpdateWatchDeathTransition(Level level)
    {
        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.None)
            return false;

        PlayerLocation current = PlayerLocation.FetchFrom(level.Session);
        if (playerWatching is null || watchPlaybackLocation.Map != current.Map)
        {
            CancelWatchDeathTransition(level);
            return false;
        }
        if (watchPlaybackLocation != current)
        {
            // A PlayerSeeker Void ending is a death followed by a direct room
            // load. Preserve the death state while the normal watch room
            // transition catches up instead of discarding its respawn packet.
            if (watchDeathRoomUnloaded)
                CancelWatchDeathTransition(level);
            return false;
        }

        if (TryCompleteWatchCrossRoomRespawn(level))
            return false;

        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.WaitingForRespawnState
            && watchDeathWipeSignaled
            && (level.Wipe is null || level.Wipe.Completed))
            StartWatchDeathWipeOut(level);

        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.WipingOut
            && watchDeathWipe is { Completed: false, Percent: >= 1f })
        {
            BeginWatchDeathRespawnReload(level);
        }

        if (watchDeathTransitionPhase == WatchDeathTransitionPhase.WaitingForRespawnLoad
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
        if (watchDeathTransitionPhase != WatchDeathTransitionPhase.WaitingForRespawnLoad
            || Engine.Scene != level
            || playerWatching is null
            || !watchDeathRespawnStateReady
            || !watchDeathRespawnNotificationReady)
            return;

        LoadWatchDeathRespawnSceneState(level);
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

        ApplyWatchEntityState(level, allowDuringTransition: true);
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

    private void BeginWatchDeathRespawnReload(Level level)
    {
        watchDeathTransitionPhase = WatchDeathTransitionPhase.WaitingForRespawnLoad;
        if (level.Completed)
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                "Skipped unloading the completed watched room during its death transition."
            );
            return;
        }

        Session session = level.Session;
        if (session.FirstLevel
            && session.Strawberries.Count == 0
            && !session.Cassette
            && !session.HeartGem
            && !session.HitCheckpoint)
        {
            session.Time = 0L;
            session.Deaths = 0;
            level.TimerStarted = false;
        }

        session.Dashes = session.DashesAtLevelStart;
        Glitch.Value = 0f;
#pragma warning disable CS0618 // Match Celeste.Level.Reload exactly.
        Engine.TimeRate = 1f;
#pragma warning restore CS0618
        Distort.Anxiety = 0f;
        Distort.GameRate = 1f;
        Audio.SetMusicParam("fade", 1f);
        level.ParticlesBG.Clear();
        level.Particles.Clear();
        level.ParticlesFG.Clear();
        TrailManager.Clear();

        level.UnloadLevel();
        watchDeathRoomUnloaded = true;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Logger.Debug(
            LT.MiaoNetWatch,
            "Unloaded the watched death room while waiting for its post-respawn snapshot."
        );
    }

    private void LoadWatchDeathRespawnSceneState(Level level)
    {
        if (watchEntityStates is null
            || PlayerLocation.FetchFrom(level.Session) != watchEntityLocation)
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                "Skipped the watched death room reload because its complete scene state was unavailable."
            );
            CompleteWatchDeathRespawnReload(level);
            ApplyWatchEntityState(level, allowDuringTransition: true);
            return;
        }

        WatchEntityState[] states = watchEntityStates.Values.ToArray();
        WatchEntityKey persistentSessionKey = new(WatchEntityKind.PersistentSession, 0);
        bool sessionPrepared = watchEntityStates.TryGetValue(
            persistentSessionKey,
            out WatchEntityState persistentSessionState
        ) && WatchPersistentSessionAdapter.TryApplySessionState(level, persistentSessionState);
        if (!sessionPrepared)
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Kept the watched death respawn lightweight because its authoritative " +
                $"PersistentSession state was unavailable for {watchEntityLocation.Room}."
            );
            CompleteWatchDeathRespawnReload(level);
            ApplyWatchEntityState(level, allowDuringTransition: true);
            return;
        }

        CompleteWatchDeathRespawnReload(level);

        if (Engine.Scene != level
            || PlayerLocation.FetchFrom(level.Session) != watchEntityLocation)
        {
            Logger.Error(
                LT.MiaoNetWatch,
                $"The watched death room reload did not rebuild {watchEntityLocation.Room}; " +
                "skipped applying its scene snapshot."
            );
            return;
        }

        NormalizeWatchRoomRendering(level);
        if (level.Tracker.GetEntity<Player>() is { } localPlayer)
        {
            localPlayer.Visible = false;
            localPlayer.StateMachine.State = Player.StFrozen;
        }
        else
        {
            Logger.Warn(LT.MiaoNetWatch, "The watched death room reload produced no local Player.");
        }

        watchPendingEntityStateKeys.Clear();
        watchPendingEntityStateKeys.UnionWith(watchEntityStates.Keys);
        watchPendingEntityStateMode = WatchEntityStateMode.Replace;
        watchEntityLifecycleResetPending = true;
        watchLifecycleIncompleteKinds.Clear();

        ApplyWatchEntityState(level, allowDuringTransition: true);
        Logger.Info(
            LT.MiaoNetWatch,
            "Loaded the watched room after its post-death scene snapshot became available."
        );
    }

    private void CompleteWatchDeathRespawnReload(Level level)
    {
        LoadUnloadedWatchDeathRoom(level);
    }

    private void LoadUnloadedWatchDeathRoom(Level level)
    {
        if (!watchDeathRoomUnloaded)
            return;

        level.LoadLevel(Player.IntroTypes.Respawn, false);
        level.strawberriesDisplay.DrawLerp = 0f;
        if (level.Entities.FindFirst<WindController>() is { } windController)
            windController.SnapWind();
        else
            level.Wind = Vector2.Zero;
        watchDeathRoomUnloaded = false;
    }

    private void CancelWatchDeathTransition(Level? level)
    {
        if (watchDeathRoomUnloaded
            && level is not null
            && ReferenceEquals(Engine.Scene, level))
        {
            WatchEntityKey persistentSessionKey = new(WatchEntityKind.PersistentSession, 0);
            if (watchEntityStates is not null
                && PlayerLocation.FetchFrom(level.Session) == watchEntityLocation
                && watchEntityStates.TryGetValue(
                    persistentSessionKey,
                    out WatchEntityState persistentSessionState
                ))
                WatchPersistentSessionAdapter.TryApplySessionState(level, persistentSessionState);

            LoadUnloadedWatchDeathRoom(level);
        }

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
        watchDeathRoomUnloaded = false;
    }
}
