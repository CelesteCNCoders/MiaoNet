using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private readonly HashSet<int> watchProducerSessions = new();
    private HashSet<string>? lastProducedFlags;
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
        HashSet<WatchEntityKind> unavailableKinds = WatchEntitySyncRegistry.CaptureStates(
            level,
            out Dictionary<WatchEntityKey, WatchEntityState> entityStates,
            forceCurrent: true
        );
        PreserveUnavailableWatchEntityStates(location, unavailableKinds, entityStates);
        if (unavailableKinds.Count > 0)
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Captured partial watch snapshot for session {request.SessionID}; " +
                $"quarantined={string.Join(",", unavailableKinds.Order())}."
            );
        int sequence = initializeProducer ? 0 : watchProducerSequence;
        WatchSceneSnapshot snapshot = new(
            location,
            sequence,
            flags,
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
            lastProducedEntityStates = entityStates;
        }

        watchProducerSessions.Add(request.SessionID);
        context.Response(request, new PacketWatchSnapshotResponse(WatchSnapshotResult.Success, snapshot));

        Logger.Info(
            LT.MiaoNetWatch,
            $"Captured watch snapshot for session {request.SessionID}; " +
            $"flags={flags.Length}, entities={entityStates.Count}, " +
            $"sequence={watchProducerSequence}."
        );
    }

    private void UpdateWatchSceneProducer(Level level)
    {
        if (watchProducerSessions.Count == 0
            || lastProducedFlags is null
            || lastProducedEntityStates is null)
            return;

        PlayerLocation currentLocation = PlayerLocation.FetchFrom(level.Session);
        if (currentLocation.Map != watchProducerLocation.Map)
            return;

        HashSet<string> currentFlags = new(level.Session.Flags, StringComparer.Ordinal);
        bool roomChanged = currentLocation != watchProducerLocation;
        bool requiresRoomReload = watchProducerRoomReloadPending && !roomChanged;
        bool isDeathRespawn = watchProducerDeathRespawnLocation == currentLocation;
        WatchRoomTransition? roomTransition = roomChanged
            && watchProducerPendingRoomTransition is { } pendingTransition
            && pendingTransition.SourceLocation == watchProducerLocation
            && pendingTransition.TargetLocation == currentLocation
            ? pendingTransition
            : null;
        IReadOnlyCollection<WatchEntityEvent> entityEvents = roomChanged || isDeathRespawn
            ? []
            : pendingProducedEntityEvents;
        HashSet<WatchEntityKind> unavailableKinds = WatchEntitySyncRegistry.CaptureStates(
            level,
            out Dictionary<WatchEntityKey, WatchEntityState> currentEntityStates,
            forceCurrent: roomChanged || requiresRoomReload || isDeathRespawn
        );
        PreserveUnavailableWatchEntityStates(currentLocation, unavailableKinds, currentEntityStates);
        if (unavailableKinds.Count > 0)
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Skipped invalid watch entity updates; " +
                $"quarantined={string.Join(",", unavailableKinds.Order())}."
            );
        WatchSceneDelta? delta = WatchSceneDelta.Create(
            watchProducerSequence + 1,
            currentLocation,
            lastProducedFlags,
            currentFlags,
            lastProducedEntityStates,
            currentEntityStates,
            entityEvents,
            roomChanged || isDeathRespawn,
            requiresRoomReload,
            isDeathRespawn,
            roomTransition
        );
        if (delta is null)
            return;

        if (!WatchPacketValidator.IsValid(delta))
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                "Discarded an invalid watch delta and retrying as an event-free complete state."
            );
            pendingProducedEntityEvents.Clear();
            delta = WatchSceneDelta.Create(
                watchProducerSequence + 1,
                currentLocation,
                lastProducedFlags,
                currentFlags,
                lastProducedEntityStates,
                currentEntityStates,
                [],
                forceEntityState: true,
                requiresRoomReload,
                isDeathRespawn,
                roomTransition
            );
            if (delta is null || !WatchPacketValidator.IsValid(delta))
            {
                Logger.Warn(
                    LT.MiaoNetWatch,
                    "Skipped an invalid watch scene update; the current session remains active."
                );
                return;
            }
        }

        PlayerLocation previousLocation = watchProducerLocation;
        watchProducerSequence = delta.Sequence;
        watchProducerLocation = currentLocation;
        lastProducedFlags = currentFlags;
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
        lastProducedEntityStates = null;
        pendingProducedEntityEvents.Clear();
        watchProducerLocation = default;
        watchProducerSequence = 0;
        watchProducerRoomReloadPending = false;
        watchProducerEntityResyncPending = false;
        watchProducerDeathRespawnLocation = null;
        watchProducerPendingRoomTransition = null;
    }

    private void PreserveUnavailableWatchEntityStates(
        PlayerLocation location,
        IReadOnlySet<WatchEntityKind> unavailableKinds,
        IDictionary<WatchEntityKey, WatchEntityState> currentStates
    )
    {
        if (unavailableKinds.Count == 0
            || location != watchProducerLocation
            || lastProducedEntityStates is null)
            return;

        int preserved = 0;
        foreach ((WatchEntityKey key, WatchEntityState state) in lastProducedEntityStates)
        {
            if (!unavailableKinds.Contains(key.Kind))
                continue;
            currentStates[key] = state;
            preserved++;
        }
        if (preserved > 0)
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Preserved {preserved} last-known watch entity states after adapter quarantine."
            );
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
}
