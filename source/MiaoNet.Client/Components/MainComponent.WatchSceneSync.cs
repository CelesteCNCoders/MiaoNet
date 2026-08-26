using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private readonly HashSet<int> watchProducerSessions = new();
    private HashSet<string>? lastProducedFlags;
    private HashSet<int>? lastProducedActiveTouchSwitchIDs;
    private Dictionary<WatchEntityKey, WatchEntityState>? lastProducedEntityStates;
    private readonly List<WatchEntityEvent> pendingProducedEntityEvents = new();
    private PlayerLocation watchProducerLocation;
    private int watchProducerSequence;
    private bool watchProducerRoomReloadPending;
    private bool watchProducerEntityResyncPending;
    private PlayerLocation? watchProducerDeathRespawnLocation;
    private WatchRoomTransition? watchProducerPendingRoomTransition;

    private void Context_WatchSnapshotRequested(PacketWatchSnapshotRequest request)
    {
        if (Engine.Scene is not Level level)
        {
            context.Response(request, new PacketWatchSnapshotResponse(WatchSnapshotResult.Unavailable, null));
            return;
        }

        PlayerLocation location = PlayerLocation.FetchFrom(level.Session);
        if (location != request.ExpectedLocation)
        {
            context.Response(request, new PacketWatchSnapshotResponse(WatchSnapshotResult.LocationChanged, null));
            return;
        }

        bool initializeProducer = watchProducerSessions.Count == 0;
        if (!initializeProducer)
            UpdateWatchSceneProducer(level);

        string[] flags = level.Session.Flags.Order(StringComparer.Ordinal).ToArray();
        int[] activeTouchSwitchIDs = FetchActiveTouchSwitchIDs(level).Order().ToArray();
        Dictionary<WatchEntityKey, WatchEntityState> entityStates =
            WatchEntitySyncRegistry.CaptureStates(level, forceCurrent: true);
        int sequence = initializeProducer ? 0 : watchProducerSequence;
        WatchSceneSnapshot snapshot = new(
            location,
            sequence,
            flags,
            activeTouchSwitchIDs,
            WatchSceneDelta.OrderEntityStates(entityStates.Values)
        );
        if (!WatchPacketValidator.IsValid(snapshot))
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Cannot capture watch snapshot for session {request.SessionID}: scene state is too large."
            );
            context.Response(request, new PacketWatchSnapshotResponse(WatchSnapshotResult.Unavailable, null));
            return;
        }

        if (initializeProducer)
        {
            watchProducerLocation = location;
            watchProducerSequence = 0;
            lastProducedFlags = new(level.Session.Flags, StringComparer.Ordinal);
            lastProducedActiveTouchSwitchIDs = activeTouchSwitchIDs.ToHashSet();
            lastProducedEntityStates = entityStates;
        }

        watchProducerSessions.Add(request.SessionID);
        context.Response(request, new PacketWatchSnapshotResponse(WatchSnapshotResult.Success, snapshot));

        Logger.Info(
            LT.MiaoNetWatch,
            $"Captured watch snapshot for session {request.SessionID}; " +
            $"flags={flags.Length}, touchSwitches={activeTouchSwitchIDs.Length}, " +
            $"entities={entityStates.Count}, " +
            $"sequence={watchProducerSequence}."
        );
    }

    private void UpdateWatchSceneProducer(Level level)
    {
        if (watchProducerSessions.Count == 0
            || lastProducedFlags is null
            || lastProducedActiveTouchSwitchIDs is null
            || lastProducedEntityStates is null)
            return;

        PlayerLocation currentLocation = PlayerLocation.FetchFrom(level.Session);
        if (currentLocation.Map != watchProducerLocation.Map)
            return;

        HashSet<string> currentFlags = new(level.Session.Flags, StringComparer.Ordinal);
        HashSet<int> currentActiveTouchSwitchIDs = FetchActiveTouchSwitchIDs(level);
        bool roomChanged = currentLocation != watchProducerLocation;
        bool requiresRoomReload = watchProducerRoomReloadPending && !roomChanged;
        bool isDeathRespawn = watchProducerDeathRespawnLocation == currentLocation;
        bool forceLightweightResync = watchProducerEntityResyncPending
            && isDeathRespawn
            && !roomChanged;
        WatchRoomTransition? roomTransition = roomChanged
            && watchProducerPendingRoomTransition is { } pendingTransition
            && pendingTransition.SourceLocation == watchProducerLocation
            && pendingTransition.TargetLocation == currentLocation
            ? pendingTransition
            : null;
        IReadOnlyCollection<WatchEntityEvent> entityEvents = roomChanged || isDeathRespawn
            ? []
            : pendingProducedEntityEvents;
        Dictionary<WatchEntityKey, WatchEntityState> currentEntityStates =
            WatchEntitySyncRegistry.CaptureStates(
                level,
                forceCurrent: roomChanged || requiresRoomReload || isDeathRespawn
            );
        WatchSceneDelta? delta = WatchSceneDelta.Create(
            watchProducerSequence + 1,
            currentLocation,
            lastProducedFlags,
            currentFlags,
            lastProducedActiveTouchSwitchIDs,
            currentActiveTouchSwitchIDs,
            lastProducedEntityStates,
            currentEntityStates,
            entityEvents,
            roomChanged || isDeathRespawn,
            roomChanged || isDeathRespawn,
            requiresRoomReload,
            isDeathRespawn,
            roomTransition
        );
        if (delta is null)
            return;

        if (!WatchPacketValidator.IsValid(delta))
        {
            Logger.Warn(LT.MiaoNetWatch, "Stopped producing watch state: scene delta is invalid or too large.");
            foreach (int sessionID in watchProducerSessions)
                context.QueuePacket(new PacketWatchProducerStop(sessionID));
            ClearWatchSceneProducer();
            return;
        }

        PlayerLocation previousLocation = watchProducerLocation;
        watchProducerSequence = delta.Sequence;
        watchProducerLocation = currentLocation;
        lastProducedFlags = currentFlags;
        lastProducedActiveTouchSwitchIDs = currentActiveTouchSwitchIDs;
        lastProducedEntityStates = currentEntityStates;
        pendingProducedEntityEvents.Clear();
        watchProducerRoomReloadPending = false;
        watchProducerEntityResyncPending = false;
        if (isDeathRespawn)
            watchProducerDeathRespawnLocation = null;
        if (roomChanged)
            watchProducerPendingRoomTransition = null;
        context.QueuePacket(new PacketWatchSceneDelta(delta));
        if (isDeathRespawn)
        {
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Emitted {(roomChanged ? "cross-room" : "lightweight")} post-respawn watch state " +
                $"for {(roomChanged ? $"{previousLocation.Room} -> " : string.Empty)}{currentLocation.Room}; " +
                $"entities={currentEntityStates.Count}, sequence={delta.Sequence}."
            );
        }
    }

    private void Context_WatchProducerStopped(PacketWatchProducerStop packet)
    {
        if (!watchProducerSessions.Remove(packet.SessionID))
            return;

        Logger.Info(LT.MiaoNetWatch, $"Stopped producing watch state for session {packet.SessionID}.");
        if (watchProducerSessions.Count == 0)
            ClearWatchSceneProducer();
    }

    private void ClearWatchSceneProducer()
    {
        watchProducerSessions.Clear();
        lastProducedFlags = null;
        lastProducedActiveTouchSwitchIDs = null;
        lastProducedEntityStates = null;
        pendingProducedEntityEvents.Clear();
        watchProducerLocation = default;
        watchProducerSequence = 0;
        watchProducerRoomReloadPending = false;
        watchProducerEntityResyncPending = false;
        watchProducerDeathRespawnLocation = null;
        watchProducerPendingRoomTransition = null;
    }

    private void MarkWatchProducerRoomReload(PlayerLocation location)
    {
        if (watchProducerSessions.Count > 0 && location == watchProducerLocation)
            watchProducerRoomReloadPending = true;
    }

    private void MarkWatchProducerEntityResync(PlayerLocation location)
    {
        if (watchProducerSessions.Count <= 0 || location.Map != watchProducerLocation.Map)
            return;

        // PlayerSeeker.End creates the target-room Player before Everest raises
        // OnLoadLevel. Keep the respawn target independently from the producer's
        // previous room so the ensuing room Replace can carry IsDeathRespawn.
        watchProducerDeathRespawnLocation = location;
        watchProducerEntityResyncPending = location == watchProducerLocation;
        watchProducerRoomReloadPending = false;
        watchProducerPendingRoomTransition = null;
        pendingProducedEntityEvents.Clear();
    }

    private void MiaoNetModule_PlayerRoomTransition(
        Level level,
        LevelData next,
        Player player,
        Vector2 direction
    )
    {
        if (watchProducerSessions.Count == 0 || Watching)
            return;

        PlayerLocation source = PlayerLocation.FetchFrom(level.Session);
        if (source != watchProducerLocation)
            return;

        watchProducerPendingRoomTransition = new WatchRoomTransition(
            source,
            new PlayerLocation(source.Map, next.Name),
            player.Position,
            direction
        );
        Logger.Debug(
            LT.MiaoNetWatch,
            $"Captured Player room transition {source.Room} -> {next.Name}, " +
            $"direction={direction}, player={player.Position}."
        );
    }

    private void WatchEntitySyncRegistry_EventProduced(Level level, WatchEntityEvent entityEvent)
    {
        if (watchProducerSessions.Count == 0
            || PlayerLocation.FetchFrom(level.Session) != watchProducerLocation)
            return;

        pendingProducedEntityEvents.Add(entityEvent);
    }

    private static HashSet<int> FetchActiveTouchSwitchIDs(Level level)
    {
        HashSet<int> activeTouchSwitchIDs = new();
        foreach (TouchSwitch touchSwitch in level.Tracker.GetEntities<TouchSwitch>().Cast<TouchSwitch>())
        {
            if (touchSwitch.Switch.Activated
                && TouchSwitchIDTracker.TryGetID(touchSwitch, level.Session.Level, out int id))
                activeTouchSwitchIDs.Add(id);
        }
        return activeTouchSwitchIDs;
    }
}
