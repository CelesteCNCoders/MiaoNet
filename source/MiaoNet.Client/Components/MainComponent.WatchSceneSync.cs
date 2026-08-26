using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private readonly HashSet<int> watchProducerSessions = new();
    private HashSet<string>? lastProducedFlags;
    private HashSet<int>? lastProducedActiveTouchSwitchIDs;
    private PlayerLocation watchProducerLocation;
    private int watchProducerSequence;
    private bool watchProducerRoomReloadPending;

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
        int sequence = initializeProducer ? 0 : watchProducerSequence;
        WatchSceneSnapshot snapshot = new(location, sequence, flags, activeTouchSwitchIDs);
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
        }

        watchProducerSessions.Add(request.SessionID);
        context.Response(request, new PacketWatchSnapshotResponse(WatchSnapshotResult.Success, snapshot));

        Logger.Info(
            LT.MiaoNetWatch,
            $"Captured watch snapshot for session {request.SessionID}; " +
            $"flags={flags.Length}, touchSwitches={activeTouchSwitchIDs.Length}, " +
            $"sequence={watchProducerSequence}."
        );
    }

    private void UpdateWatchSceneProducer(Level level)
    {
        if (watchProducerSessions.Count == 0
            || lastProducedFlags is null
            || lastProducedActiveTouchSwitchIDs is null)
            return;

        PlayerLocation currentLocation = PlayerLocation.FetchFrom(level.Session);
        if (currentLocation.Map != watchProducerLocation.Map)
            return;

        HashSet<string> currentFlags = new(level.Session.Flags, StringComparer.Ordinal);
        HashSet<int> currentActiveTouchSwitchIDs = FetchActiveTouchSwitchIDs(level);
        bool roomChanged = currentLocation != watchProducerLocation;
        bool requiresRoomReload = watchProducerRoomReloadPending && !roomChanged;
        WatchSceneDelta? delta = WatchSceneDelta.Create(
            watchProducerSequence + 1,
            currentLocation,
            lastProducedFlags,
            currentFlags,
            lastProducedActiveTouchSwitchIDs,
            currentActiveTouchSwitchIDs,
            roomChanged,
            requiresRoomReload
        );
        if (delta is null)
            return;

        if (!WatchPacketValidator.IsValid(delta))
        {
            Logger.Warn(LT.MiaoNetWatch, "Stopped producing watch state: scene delta is too large.");
            foreach (int sessionID in watchProducerSessions)
                context.QueuePacket(new PacketWatchProducerStop(sessionID));
            ClearWatchSceneProducer();
            return;
        }

        watchProducerSequence = delta.Sequence;
        watchProducerLocation = currentLocation;
        lastProducedFlags = currentFlags;
        lastProducedActiveTouchSwitchIDs = currentActiveTouchSwitchIDs;
        watchProducerRoomReloadPending = false;
        context.QueuePacket(new PacketWatchSceneDelta(delta));
        Logger.Debug(
            LT.MiaoNetWatch,
            $"Emitted watch delta sequence {delta.Sequence}; " +
            $"room={delta.Location.Room}, added={delta.AddedFlags.Count}, " +
            $"removed={delta.RemovedFlags.Count}, " +
            $"roomReload={delta.RequiresRoomReload}, " +
            $"touchSwitchState={delta.HasTouchSwitchState}, " +
            $"activeTouchSwitches={delta.ActiveTouchSwitchIDs.Count}."
        );
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
        watchProducerLocation = default;
        watchProducerSequence = 0;
        watchProducerRoomReloadPending = false;
    }

    private void MarkWatchProducerRoomReload(PlayerLocation location)
    {
        if (watchProducerSessions.Count > 0 && location == watchProducerLocation)
            watchProducerRoomReloadPending = true;
    }

    private static HashSet<int> FetchActiveTouchSwitchIDs(Level level)
    {
        HashSet<int> activeTouchSwitchIDs = new();
        foreach (TouchSwitch touchSwitch in level.Tracker.GetEntities<TouchSwitch>().Cast<TouchSwitch>())
        {
            if (touchSwitch.Switch.Activated
                && TouchSwitchIDTracker.TryGetID(touchSwitch, out int id))
                activeTouchSwitchIDs.Add(id);
        }
        return activeTouchSwitchIDs;
    }
}
