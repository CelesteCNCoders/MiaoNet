using Microsoft.Extensions.Logging;
using MiaoNet.Shared;
using System.Diagnostics;
using System.Buffers;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    private void RegisterPacketHandlers(PacketHandlerRegister r)
    {
        r.Register<PacketPlayerFrame>(HandlePacketAsync);
        r.Register<PacketPlayerLocationChanged>(HandlePacketAsync);
        r.Register<PacketPlayerChannelMove>(HandlePacketAsync);
        r.Register<PacketSendChatMessage>(HandlePacketAsync);
        r.Register<PacketSendEmote>(HandlePacketAsync);
        r.Register<PacketSendEmoteText>(HandlePacketAsync);
        r.Register<PacketPlayerLiveState>(HandlePacketAsync);
        r.Register<PacketUpdateGlobalFlag>(HandlePacketAsync);
        r.Register<PacketTeleportRequest>(HandlePacketAsync);
        r.Register<PacketSendPrivateChatMessage>(HandlePacketAsync);
        r.Register<PacketPlayerPlayedAudio>(HandlePacketAsync);
        r.Register<PacketPlayerGrabPlayer>(HandlePacketAsync);
        r.Register<PacketPlayerGrabJumpOut>(HandlePacketAsync);
        r.Register<PacketCreateFireworks>(HandlePacketAsync);
        r.Register<PacketWatchStart>(HandlePacketAsync);
        r.Register<PacketWatchSceneDelta>(HandlePacketAsync);
        r.Register<PacketWatchResyncRequest>(HandlePacketAsync);
        r.Register<PacketWatchStop>(HandlePacketAsync);
        r.Register<PacketWatchProducerStop>(HandlePacketAsync);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerFrame packet)
    {
        var player = connection.Player;
        if (player.State is null)
        {
            logger.LogError(AppEvents.Game, "Packet frame received but no initial state for {p}.", player.Info);
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }
        else if (!player.Location.IsInMap)
        {
            logger.LogError(AppEvents.Game, "Player {p} is not in map but sent PacketPlayerFrame!", player.Info);
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }

        var delta = packet.StateDelta;

        if (!PlayerPacketValidator.HasValidFollowerCount(delta))
        {
            logger.LogWarning(AppEvents.Game, "Player {p} sent too many followers in a frame.", player.Info);
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Too many followers");
            return;
        }
        if (!PlayerPacketValidator.HasValidCameraPosition(delta))
        {
            logger.LogWarning(AppEvents.Game, "Player {p} sent a non-finite camera position.", player.Info);
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }

        // TODO we can actually using one Task for one Map
        // to handle these updates lock-free
        ServerMap u = player.Channel.Maps[player.Location.Map];
        using (u.StateLock.AcquireReadLock())
        {
            var state = player.State;
            state.ApplyDelta(delta);
        }
        PacketContextualPlayerNotification<PacketPlayerFrame> notification = new(connection.ID, packet);
        if (!delta.HasCameraPosition)
        {
            await BroadcastToScopeExceptAsync(notification, u, connection.ID);
            return;
        }

        Task watcherTask;
        Task otherPlayersTask = Task.CompletedTask;
        using (stateLock.AcquireReadLock())
        {
            IReadOnlyCollection<WatchSession> targetSessions = watchSessions.GetByTarget(connection.ID);
            watcherTask = BroadcastToScopeExceptAsync(
                notification,
                u,
                connection.ID,
                candidate => PlayerFrameRouting.IsActiveWatcher(
                    targetSessions,
                    candidate.ID,
                    u.MapLocation
                )
            );

            bool hasOtherPlayers = u.Players.Any(candidate =>
                candidate.ID != connection.ID
                && !PlayerFrameRouting.IsActiveWatcher(
                    targetSessions,
                    candidate.ID,
                    u.MapLocation
                )
            );
            if (hasOtherPlayers)
            {
                PacketPlayerFrame strippedFrame = PlayerFrameRouting.CreateWithoutCamera(packet);
                PacketContextualPlayerNotification<PacketPlayerFrame> strippedNotification =
                    new(connection.ID, strippedFrame);
                otherPlayersTask = BroadcastToScopeExceptAsync(
                    strippedNotification,
                    u,
                    connection.ID,
                    candidate => !PlayerFrameRouting.IsActiveWatcher(
                        targetSessions,
                        candidate.ID,
                        u.MapLocation
                    )
                );
            }
        }

        await Task.WhenAll(watcherTask, otherPlayersTask);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerLocationChanged packet)
    {
        var player = connection.Player;
        var oldLocation = player.Location;
        var newLocation = packet.Location;

        if (oldLocation != newLocation)
        {
            bool targetMapChanged;
            bool watcherMapChanged;
            using (stateLock.AcquireReadLock())
            {
                bool mapChanged = oldLocation.Map != newLocation.Map;
                targetMapChanged = mapChanged && watchSessions.HasTarget(connection.ID);
                watcherMapChanged = mapChanged && watchSessions.HasWatcher(connection.ID);
            }
            if (targetMapChanged || watcherMapChanged)
                await EndWatchSessionsForPlayerAsync(connection, WatchEndReason.LocationChanged, false);
        }
        logger.LogDebug(
            AppEvents.GameState,
            "Player {p} location changing from {p1} to {p2}.",
            player.Info, oldLocation, newLocation
        );

        // went to somewhere like debug map or menu
        if (!newLocation.IsInMap)
        {
            Task othersTask;
            ValueTask debugSnapshotTask = default;
            using (stateLock.AcquireWriteLock())
            {
                othersTask = BroadcastToScopeExceptAsync(
                    new PacketPlayerLocationChangedNotification(player.ID, newLocation, null),
                    player.Channel,
                    connection.ID
                );

                // if the player is going to debug map
                // sending states here is necessary currently
                if (newLocation.IsInDebugMap && player.Channel.Maps.TryGetValue(newLocation.Map, out var mapTo))
                {
                    mapTo.StateLock.EnterWriteLock();
                    try
                    {
                        var mapPlayers = mapTo.GetPlayerMovedInitialDatas(connection);
                        debugSnapshotTask = connection.QueuePacketAsync(
                            new PacketPlayerLocationChangedResponse(mapPlayers));
                    }
                    finally
                    {
                        mapTo.StateLock.ExitWriteLock();
                    }
                }

                player.Channel.OnPlayerMapMove(connection, oldLocation.Map, newLocation.Map);
                player.Location = newLocation;
                player.State = null;
            }

            await othersTask;
            await debugSnapshotTask;
            return;
        }

        // just changed room, no need to send state
        if (oldLocation.IsInMap && oldLocation.Map == newLocation.Map && packet.InitialState is null)
        {
            player.Location = newLocation;
            await BroadcastToScopeExceptAsync(
                new PacketPlayerLocationChangedNotification(player.ID, newLocation, null),
                player.Channel,
                connection.ID
            );
            return;
        }

        // now the initial state is necessary
        // note that map reentering is supported, so "oldLocation.Map == newLocation.Map" can be true here
        if (packet.InitialState is null)
        {
            logger.LogWarning(
                AppEvents.GameState,
                "Player {p} didn't send state when went to {loc}.",
                player.Info, newLocation
            );
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }
        if (!PlayerPacketValidator.HasValidFollowerCount(packet.InitialState))
        {
            logger.LogWarning(AppEvents.GameState, "Player {p} sent too many followers in its initial state.", player.Info);
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Too many followers");
            return;
        }
        Debug.Assert(newLocation.IsInMap);
        Task generalTask, withStateTask;
        ValueTask responseTask = default;

        using (stateLock.AcquireWriteLock())
        {
            var c = player.Channel;
            c.Maps.TryGetValue(newLocation.Map, out var mapTo);

            mapTo?.StateLock.EnterWriteLock();
            try
            {
                var generalPacket = new PacketPlayerLocationChangedNotification(player.ID, newLocation, null);
                var withStatePacket = new PacketPlayerLocationChangedNotification(player.ID, newLocation, packet.InitialState);

                var mapPlayers = mapTo?.GetPlayerMovedInitialDatas(connection) ?? [];
                var responsePacket = new PacketPlayerLocationChangedResponse(mapPlayers);

                generalTask = mapTo is not null
                    ? BroadcastToScopeExceptAsync(generalPacket, player.Channel, connection.ID, c => !mapTo.Players.Contains(c))
                    : BroadcastToScopeExceptAsync(generalPacket, player.Channel, connection.ID);

                withStateTask = mapTo is not null
                    ? BroadcastToScopeExceptAsync(withStatePacket, mapTo, connection.ID)
                    : Task.CompletedTask;
                responseTask = connection.QueuePacketAsync(responsePacket);

                c.OnPlayerMapMove(connection, oldLocation.Map, newLocation.Map);
                player.Location = newLocation;
                player.State = packet.InitialState;
            }
            finally
            {
                mapTo?.StateLock.ExitWriteLock();
            }
        }

        await generalTask;
        await withStateTask;
        await responseTask;
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerChannelMove packet)
    {
        var player = connection.Player;

        ValueTask responseTask;
        Task sameMapTask;
        Task sameChannelTask;
        Task crossChannelTask;
        Task createdBroadcastTask = Task.CompletedTask;
        ValueTask createdTask = default;
        bool notifyChannelCreated = false;
        List<(MiaoClientConnection Connection, IContextualPacket Packet)> watchNotifications = new();
        int endedWatchSessions = 0;

        using (stateLock.AcquireWriteLock())
        {
            if (!serverState.TryGetChannelByName(packet.TargetChannelName, out ServerChannel? targetChannel))
            {
                // not found, create a new channel with the given name
                targetChannel = serverState.CreateNewChannel(new ChannelInfo(packet.TargetChannelName));
                serverState.AddChannel(targetChannel);

                if (targetChannel.IsPrivate)
                {
                    // tell only the creator this channel is created
                    notifyChannelCreated = true;
                }
                else
                {
                    // tell everyone
                    createdBroadcastTask = BroadcastToScopeAsync(
                        new PacketChannelCreated(targetChannel.ID, targetChannel.Info),
                        serverState
                    );
                }
            }
            else if (targetChannel.IsPrivate && !targetChannel.Players.Contains(connection))
            {
                // channel is private, and the player is not in it
                // tell the player they should create the channel locally
                notifyChannelCreated = true;
            }

            if (targetChannel != player.Channel)
            {
                endedWatchSessions = RemoveWatchSessionsForPlayerLocked(
                    connection,
                    WatchEndReason.ChannelChanged,
                    connectionClosing: false,
                    watchNotifications
                );
            }

            targetChannel.Maps.TryGetValue(player.Location.Map, out ServerMap? mapTo);
            mapTo?.StateLock.EnterWriteLock();
            try
            {
                var channelPlayers = new List<PlayerPresenceDataWithID>(targetChannel.Players.Count);
                foreach (var c in targetChannel.Players)
                {
                    if (c.ID == connection.ID)
                        continue;
                    channelPlayers.Add(new PlayerPresenceDataWithID(
                        c.ID, new PlayerPresenceData(c.Player.Location, c.Player.GlobalFlags)
                    ));
                }
                var mapPlayers = mapTo?.GetPlayerMovedInitialDatas(connection);

                if (notifyChannelCreated)
                    createdTask = connection.QueuePacketAsync(new PacketChannelCreated(targetChannel.ID, targetChannel.Info));

                var responsePacket = new PacketPlayerChannelMovedResponse(targetChannel.ID, mapPlayers, channelPlayers);
                responseTask = connection.QueuePacketAsync(responsePacket);

                // same-map players in the target channel get state + presence
                var sameMapNotification = new PacketPlayerChannelMovedNotification(
                    connection.ID,
                    targetChannel.ID,
                    player.State is null ? null : new PlayerMovedInitialData(player.State),
                    new PlayerPresenceData(player.Location, player.GlobalFlags)
                );
                sameMapTask = mapTo is not null
                    ? BroadcastToScopeExceptAsync(sameMapNotification, mapTo, connection.ID)
                    : Task.CompletedTask;

                var sameChannelNotification = new PacketPlayerChannelMovedNotification(
                    connection.ID,
                    targetChannel.ID,
                    null,
                    new PlayerPresenceData(player.Location, player.GlobalFlags)
                );
                sameChannelTask = BroadcastToScopeExceptAsync(
                    sameChannelNotification,
                    targetChannel,
                    connection.ID,
                    c => mapTo is null || !mapTo.Players.Contains(c)
                );

                // players in other channels get only a "moved" notification
                // and for private channels, the virtual id is used instead of the real channel id
                var crossChannelNotification = new PacketPlayerChannelMovedNotification(
                    connection.ID,
                    targetChannel.IsPrivate ? ChannelInfo.PrivateChannelVirtualID : targetChannel.ID
                );
                crossChannelTask = BroadcastToScopeExceptAsync(
                    crossChannelNotification,
                    serverState,
                    connection.ID,
                    c => c.Player.Channel != targetChannel
                );
            }
            finally
            {
                mapTo?.StateLock.ExitWriteLock();
            }

            serverState.PlayerChannelMove(connection, player.Channel, targetChannel);
        }

        await SendWatchSessionEndNotificationsAsync(
            connection,
            WatchEndReason.ChannelChanged,
            connectionClosing: false,
            endedWatchSessions,
            watchNotifications
        );
        if (notifyChannelCreated)
            await createdTask;
        await createdBroadcastTask;
        await responseTask;
        await sameMapTask;
        await sameChannelTask;
        await crossChannelTask;
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendChatMessage packet)
    {
        logger.LogInformation(AppEvents.GameChat, "[{channel}] {player}: {msg}", packet.ChatChannel, connection.Player.Info, packet.Content);
        if (packet.Content.Length > 64)
        {
            logger.LogWarning(AppEvents.GameChat, "{player} is sending a large chat!", connection.Player.Info);
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Chat too long.");
            return;
        }
        ChatMessageType type = packet.ChatChannel switch
        {
            ChatChannel.Global => ChatMessageType.Chat,
            ChatChannel.Channel => ChatMessageType.ChannelChat,
            ChatChannel.Map => ChatMessageType.MapChat,
            _ => ChatMessageType.Chat
        };
        var toSend = new PacketChatMessage(DateTime.UtcNow, type, connection.Player.ID, packet.Content);
        switch (type)
        {
        case ChatMessageType.Chat:
            await BroadcastToScopeAsync(toSend, serverState);
            break;
        case ChatMessageType.ChannelChat:
            await BroadcastToScopeAsync(toSend, connection.Player.Channel);
            break;
        case ChatMessageType.MapChat:
            await BroadcastToScopeAsync(
                toSend,
                connection.Player.Channel,
                c => c.Player.Location.Map == connection.Player.Location.Map
            );
            break;
        default:
            goto case ChatMessageType.Chat;
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendEmote packet)
    {
        await BroadcastToScopeExceptAsync(
            new PacketEmote(connection.ID, packet.Emote),
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendEmoteText packet)
    {
        await BroadcastToScopeExceptAsync(
            new PacketEmoteText(connection.ID, packet.Text),
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerLiveState packet)
    {
        if (packet.Type == LiveStateType.DeathWipe)
        {
            List<MiaoClientConnection> watchers = new();
            using (stateLock.AcquireReadLock())
            {
                foreach (WatchSession session in watchSessions.GetByTarget(connection.ID))
                {
                    if (session.IsActive
                        && serverState.Players.TryGetValue(session.WatcherID, out MiaoClientConnection? watcher))
                        watchers.Add(watcher);
                }
            }

            PacketPlayerNotification<PacketPlayerLiveState> notification = new(connection.ID, packet);
            foreach (MiaoClientConnection watcher in watchers)
                await watcher.QueuePacketAsync(notification);
            return;
        }

        await BroadcastToScopeExceptAsync(
            new PacketPlayerNotification<PacketPlayerLiveState>(connection.ID, packet),
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketUpdateGlobalFlag packet)
    {
        if (packet.Flags.HasFlag(PlayerGlobalFlags.Watching))
        {
            bool isTarget;
            using (stateLock.AcquireReadLock())
                isTarget = watchSessions.HasTarget(connection.ID);
            if (isTarget)
                await EndWatchSessionsForPlayerAsync(connection, WatchEndReason.TargetBeganWatching, false);
        }

        connection.Player.GlobalFlags = packet.Flags;
        await BroadcastToScopeExceptAsync(
            new PacketPlayerNotification<PacketUpdateGlobalFlag>(connection.ID, packet),
            connection.Player.Channel,
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketTeleportRequest request)
    {
        // teleporting is only allowed within the same channel
        if (ServerState.Players.TryGetValue(request.TargetPlayerID, out var target)
            && target.Player.Channel == connection.Player.Channel)
        {
            logger.LogInformation(AppEvents.Game, "{p} is requesting to teleport to {p2}.", connection.Player.Info, target.Player.Info);
            bool accepted = await target.RequestAsync(
                new PacketBeTeleportedRequest(connection.ID),
                OnOtherResponse,
                RequestTimeout,
                OnOtherTimeout
            );
            if (!accepted)
            {
                logger.LogInformation(
                    AppEvents.Game,
                    "{p}'s teleport request to {target} could not be sent because the target has too many pending requests.",
                    connection.Player.Info,
                    target.Player.Info
                );
                await connection.ResponseAsync(
                    request,
                    new(PacketTeleportResponse.TeleportFailedReason.OtherDoesNotResponse, null)
                );
            }

            Task OnOtherResponse(PacketBeTeleportedResponse response)
            {
                if (response.Accepted)
                {
                    logger.LogInformation(AppEvents.Game, "{p}'s teleport request to {p2} accepted.", connection.Player.Info, target.Player.Info);
                    return connection.ResponseAsync(
                        request,
                        new(PacketTeleportResponse.TeleportFailedReason.None, response.Session)
                    ).AsTask();
                }
                else
                {
                    logger.LogInformation(AppEvents.Game, "{p}'s teleport request to {p2} rejected.", connection.Player.Info, target.Player.Info);
                    return connection.ResponseAsync(
                        request,
                        new(PacketTeleportResponse.TeleportFailedReason.OtherDenied, null)
                    ).AsTask();
                }
            }

            Task OnOtherTimeout()
            {
                logger.LogInformation(
                    AppEvents.Game,
                    "{p}'s teleport request to {p2} timed out.",
                    connection.Player.Info,
                    target.Player.Info
                );
                return connection.ResponseAsync(
                    request,
                    new(PacketTeleportResponse.TeleportFailedReason.OtherDoesNotResponse, null)
                ).AsTask();
            }
        }
        else
        {
            logger.LogInformation(
                AppEvents.Game,
                "{p} is requesting to teleport to player(id: {id}) who is not found.",
                connection.Player.Info,
                request.TargetPlayerID
            );
            await connection.ResponseAsync(
                request,
                new(PacketTeleportResponse.TeleportFailedReason.NoSuchPlayer, null)
            );
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendPrivateChatMessage request)
    {
        // private messaging is allowed across channels (cross-channel players are
        // name-only, but that still lets you whisper them by name)
        if (ServerState.Players.TryGetValue(request.TargetPlayerID, out var target))
        {
            logger.LogInformation(
                AppEvents.GameChat,
                "{player} -> {target}: {msg}",
                connection.Player.Info,
                target.Player.Info,
                request.Content
             );

            await target.QueuePacketAsync(
                new PacketChatMessage(DateTime.UtcNow, ChatMessageType.PrivateMessage, connection.ID, request.Content)
            );
            await connection.ResponseAsync(request, new(DateTime.UtcNow, PacketSendPrivateChatMessageResponse.SendResult.Success));
        }
        else
        {
            logger.LogInformation(
                AppEvents.GameChat,
                "{player} tries to send private message to player(id: {id}) who is not found.",
                connection.Player.Info,
                request.TargetPlayerID
            );
            await connection.ResponseAsync(
                request,
                new(DateTime.UtcNow, PacketSendPrivateChatMessageResponse.SendResult.NoSuchPlayer)
            );
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerGrabPlayer packet)
    {
        if (!ServerState.Players.TryGetValue(packet.PlayerID, out var p))
            return;
        // Both grab and release packets are only valid inside the normal sync scope.
        if (!p.Player.ShouldSyncFrom(connection.Player))
            return;

        if (!packet.IsRelease && !PlayerInteractionValidator.CanGrab(connection.Player, p.Player))
        {
            logger.LogWarning(
                AppEvents.GameState,
                "Player {source} tried to grab {target} outside an enabled sync scope.",
                connection.Player.Info,
                p.Player.Info
            );
            return;
        }

        if (packet.IsRelease && !PlayerInteractionValidator.IsValidReleaseForce(packet.Force))
        {
            logger.LogWarning(AppEvents.GameState, "Player {source} sent an invalid release force.", connection.Player.Info);
            return;
        }

        PacketPlayerGrabPlayer send = packet.IsRelease ? new(connection.ID, packet.Force) : new(connection.ID);
        await p.QueuePacketAsync(send);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerGrabJumpOut packet)
    {
        if (!ServerState.Players.TryGetValue(packet.PlayerID, out var p))
            return;
        // holding requires the same channel and the same map
        if (p.Player.Channel != connection.Player.Channel
            || p.Player.Location.Map != connection.Player.Location.Map)
            return;
        PacketPlayerGrabJumpOut send = new(connection.ID);
        await p.QueuePacketAsync(send);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerPlayedAudio packet)
    {
        var p = new PacketContextualPlayerNotification<PacketPlayerPlayedAudio>(connection.ID, packet);
        await BroadcastToScopeExceptAsync(
            p,
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketCreateFireworks packet)
    {
        if (connection.Player.TryConsumeFireworksToken())
        {
            PacketPlayerNotification<PacketCreateFireworks> notification = new(connection.ID, packet);
            await BroadcastToScopeExceptAsync(
                notification,
                serverState,
                connection.ID,
                c => c.PlayerShouldSyncFrom(connection)
            );
        }
        else
        {
            // TODO localization
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Too many fireworks.");
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketWatchStart request)
    {
        WatchStartResult result;
        MiaoClientConnection? target = null;
        WatchSession? session = null;

        using (stateLock.AcquireWriteLock())
        {
            result = ValidateWatchStart(connection, request.TargetPlayerID, out target);
            if (result == WatchStartResult.Success)
            {
                session = watchSessions.Add(
                    connection.ID,
                    target!.ID,
                    connection.Player.Location.Map,
                    request.RequestID
                );
            }
        }

        if (result != WatchStartResult.Success)
        {
            logger.LogInformation(
                AppEvents.Watch,
                "Watch request from {watcher} to {target} rejected: {reason}.",
                connection.ID,
                request.TargetPlayerID,
                result
            );
            await connection.ResponseAsync(request, new(result, 0, null));
            return;
        }

        logger.LogInformation(
            AppEvents.Watch,
            "Watch session {session} pending: {watcher} -> {target}.",
            session!.ID,
            connection.ID,
            target!.ID
        );

        bool accepted = await target.RequestAsync(
            new PacketWatchSnapshotRequest(session.ID, target.Player.Location),
            response => HandleWatchSnapshotResponseAsync(session.ID, response),
            RequestTimeout,
            () => FailPendingWatchStartAsync(session.ID, notifyTarget: true)
        );
        if (!accepted)
            await FailPendingWatchStartAsync(session.ID, notifyTarget: false);
    }

    private WatchStartResult ValidateWatchStart(
        MiaoClientConnection watcher,
        int targetPlayerID,
        out MiaoClientConnection? target
    )
    {
        target = null;
        if (watcher.ID == targetPlayerID)
            return WatchStartResult.SelfTarget;
        if (!serverState.Players.TryGetValue(targetPlayerID, out target))
            return WatchStartResult.NoSuchPlayer;
        if (watcher.Player.Channel != target.Player.Channel)
            return WatchStartResult.DifferentChannel;
        if (!watcher.Player.Location.IsInMap
            || !target.Player.Location.IsInMap
            || watcher.Player.Location.Map != target.Player.Location.Map)
            return WatchStartResult.DifferentMap;
        if (!WatchProtocolCompatibility.CanUseWatchSceneSync(
            ServerFeatureFlags.WatchSceneSync,
            watcher.Player.GlobalFlags,
            target.Player.GlobalFlags
        ))
            return WatchStartResult.UnsupportedProtocol;
        if (target.Player.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching)
            || watchSessions.HasWatcher(target.ID))
            return WatchStartResult.TargetIsWatching;
        if (watchSessions.HasWatcher(watcher.ID) || watchSessions.HasTarget(watcher.ID))
            return WatchStartResult.InvalidState;
        return WatchStartResult.Success;
    }

    private async Task FailPendingWatchStartAsync(int sessionID, bool notifyTarget)
    {
        WatchSession? removedSession;
        MiaoClientConnection? watcher = null;
        MiaoClientConnection? target = null;

        using (stateLock.AcquireWriteLock())
        {
            if (!watchSessions.TryGet(sessionID, out WatchSession? session)
                || session is null
                || session.IsActive
                || !watchSessions.Remove(sessionID, out removedSession))
                return;

            serverState.Players.TryGetValue(removedSession!.WatcherID, out watcher);
            if (notifyTarget)
                serverState.Players.TryGetValue(removedSession.TargetID, out target);
        }

        logger.LogInformation(
            AppEvents.Watch,
            "Watch session {session} failed while waiting for its initial snapshot.",
            sessionID
        );
        if (watcher is not null)
        {
            await watcher.QueuePacketAsync(new PacketWatchStartResponse(
                WatchStartResult.TargetUnavailable,
                0,
                null
            )
            {
                RequestID = removedSession!.StartRequestID
            });
        }
        if (target is not null)
            await target.QueuePacketAsync(new PacketWatchProducerStop(sessionID));
    }

    private async Task HandleWatchSnapshotResponseAsync(int sessionID, PacketWatchSnapshotResponse response)
    {
        MiaoClientConnection? watcher = null;
        MiaoClientConnection? target = null;
        WatchSession? removedSession = null;
        WatchStartResult result = WatchStartResult.TargetUnavailable;
        int startRequestID = 0;

        using (stateLock.AcquireWriteLock())
        {
            if (!watchSessions.TryGet(sessionID, out WatchSession? session)
                || session is null
                || session.IsActive)
                return;

            startRequestID = session.StartRequestID;
            serverState.Players.TryGetValue(session.WatcherID, out watcher);
            serverState.Players.TryGetValue(session.TargetID, out target);

            if (watcher is not null
                && target is not null
                && response.IsSuccess
                && response.Snapshot.Location == target.Player.Location
                && response.Snapshot.Location.Map == session.Map
                && WatchPacketValidator.IsValid(response.Snapshot))
            {
                session.Activate(response.Snapshot.Sequence);
                result = WatchStartResult.Success;
            }
            else
            {
                watchSessions.Remove(sessionID, out removedSession);
                if (response.Result == WatchSnapshotResult.LocationChanged)
                    result = WatchStartResult.InvalidState;
            }
        }

        if (result == WatchStartResult.Success)
        {
            logger.LogInformation(
                AppEvents.Watch,
                "Watch session {session} active: {watcher} -> {target}, sequence {sequence}.",
                sessionID,
                watcher!.ID,
                target!.ID,
                response.Snapshot!.Sequence
            );
            PacketWatchStartResponse startResponse = new(result, sessionID, response.Snapshot)
            {
                RequestID = startRequestID
            };
            await watcher.QueuePacketAsync(startResponse);
            return;
        }

        if (removedSession is null)
            return;

        logger.LogInformation(
            AppEvents.Watch,
            "Watch session {session} failed while obtaining the snapshot: {reason}.",
            sessionID,
            result
        );
        if (watcher is not null)
        {
            await watcher.QueuePacketAsync(new PacketWatchStartResponse(result, 0, null)
            {
                RequestID = removedSession.StartRequestID
            });
        }
        if (target is not null)
            await target.QueuePacketAsync(new PacketWatchProducerStop(sessionID));
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketWatchSceneDelta packet)
    {
        if (!WatchPacketValidator.IsValid(packet.Delta))
        {
            logger.LogWarning(AppEvents.Watch, "Player {target} sent an invalid watch scene delta.", connection.ID);
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }

        List<(MiaoClientConnection Watcher, WatchSession Session)> recipients = new();
        bool requestResync = false;
        int gapCount = 0;
        int minimumLastSequence = int.MaxValue;
        int maximumLastSequence = int.MinValue;
        bool locationMatches;
        using (stateLock.AcquireWriteLock())
        {
            locationMatches = packet.Delta.Location == connection.Player.Location;
            if (locationMatches)
            {
                foreach (WatchSession session in watchSessions.GetByTarget(connection.ID))
                {
                    if (packet.Delta.Location.Map != session.Map
                        || !session.IsActive)
                        continue;

                    WatchSequenceResult sequenceResult = session.AcceptSequence(packet.Delta.Sequence);
                    if (sequenceResult == WatchSequenceResult.Gap)
                    {
                        requestResync = true;
                        gapCount++;
                        minimumLastSequence = Math.Min(minimumLastSequence, session.LastSequence);
                        maximumLastSequence = Math.Max(maximumLastSequence, session.LastSequence);
                    }
                    else if (sequenceResult == WatchSequenceResult.Next
                        && serverState.Players.TryGetValue(session.WatcherID, out MiaoClientConnection? watcher))
                        recipients.Add((watcher, session));
                }
            }
        }

        if (!locationMatches)
        {
            logger.LogWarning(
                AppEvents.Watch,
                "Player {target} sent watch state for {packetLocation} while located at {actualLocation}.",
                connection.ID,
                packet.Delta.Location,
                connection.Player.Location
            );
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }

        foreach (var (watcher, session) in recipients)
        {
            await watcher.QueuePacketAsync(
                new PacketWatchSceneDeltaNotification(session.ID, connection.ID, packet.Delta)
            );
        }

        if (requestResync)
        {
            logger.LogWarning(
                AppEvents.Watch,
                "Target {target} skipped to sequence {received}; paused {count} session(s) at {minimum}..{maximum} and requested one shared snapshot.",
                connection.ID,
                packet.Delta.Sequence,
                gapCount,
                minimumLastSequence,
                maximumLastSequence
            );
            await RequestWatchResyncSnapshotAsync(connection.ID);
        }

        if (recipients.Count > 0)
        {
            logger.LogDebug(
                AppEvents.Watch,
                "Watch delta from {target} sequence {sequence} routed to {count} watcher(s).",
                connection.ID,
                packet.Delta.Sequence,
                recipients.Count
            );
        }
    }

    private async Task HandlePacketAsync(
        MiaoClientConnection connection,
        PacketWatchResyncRequest packet
    )
    {
        int targetID = 0;
        bool requestResync = false;
        using (stateLock.AcquireWriteLock())
        {
            if (watchSessions.TryGetByWatcher(connection.ID, out WatchSession? session)
                && session is not null
                && session.ID == packet.SessionID)
            {
                targetID = session.TargetID;
                requestResync = session.TryBeginResync(
                    packet.LastAppliedSequence,
                    stopwatch.Elapsed,
                    WatcherResyncCooldown
                );
            }
        }

        if (!requestResync)
            return;

        logger.LogWarning(
            AppEvents.Watch,
            "Watcher {watcher} requested resynchronization for session {session} after sequence {sequence}.",
            connection.ID,
            packet.SessionID,
            packet.LastAppliedSequence
        );
        await RequestWatchResyncSnapshotAsync(targetID);
    }

    private async Task RequestWatchResyncSnapshotAsync(
        int targetID,
        bool scheduledRetry = false
    )
    {
        MiaoClientConnection? target = null;
        PacketWatchSnapshotRequest? request = null;
        WatchResyncAttempt attempt = default;
        List<(MiaoClientConnection? Watcher, WatchSession Session)> failed = new();

        using (stateLock.AcquireWriteLock())
        {
            WatchSession[] pending = watchSessions.GetByTarget(targetID)
                .Where(session => session.IsActive && session.IsResyncPending)
                .ToArray();
            if (pending.Length == 0)
            {
                watchResyncCoordinator.Complete(targetID);
                return;
            }
            if (!serverState.Players.TryGetValue(targetID, out target))
            {
                watchResyncCoordinator.Complete(targetID);
                return;
            }

            WatchResyncStartResult startResult = scheduledRetry
                ? watchResyncCoordinator.TryStartScheduled(targetID, out attempt)
                : watchResyncCoordinator.TryStart(targetID, out attempt);
            if (startResult == WatchResyncStartResult.Pending)
                return;
            if (startResult == WatchResyncStartResult.Exhausted)
            {
                foreach (WatchSession session in pending)
                {
                    if (!watchSessions.Remove(session.ID, out _))
                        continue;
                    serverState.Players.TryGetValue(session.WatcherID, out MiaoClientConnection? watcher);
                    failed.Add((watcher, session));
                }
                watchResyncCoordinator.Complete(targetID);
            }
            else
            {
                request = new PacketWatchSnapshotRequest(pending[0].ID, target.Player.Location);
            }
        }

        if (request is not null)
        {
            try
            {
                bool accepted = await target!.RequestAsync(
                    request,
                    response => HandleWatchResyncSnapshotResponseAsync(
                        attempt,
                        response
                    ),
                    WatchResyncRequestTimeout,
                    () => HandleWatchResyncRequestFailureAsync(attempt, "timed out")
                );
                if (!accepted)
                {
                    await HandleWatchResyncRequestFailureAsync(
                        attempt,
                        "was rejected because the target has too many pending requests"
                    );
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    AppEvents.Watch,
                    exception,
                    "Could not queue watch resync attempt {attempt} for target {target}.",
                    attempt.Number,
                    targetID
                );
                await HandleWatchResyncRequestFailureAsync(attempt, "could not be queued");
            }
            return;
        }

        if (failed.Count == 0)
            return;

        logger.LogWarning(
            AppEvents.Watch,
            "Stopped {count} watch session(s) after repeated resynchronization failures for target {target}.",
            failed.Count,
            targetID
        );
        foreach ((MiaoClientConnection? watcher, WatchSession session) in failed)
        {
            if (watcher is not null)
                await watcher.QueuePacketAsync(new PacketWatchEnded(session.ID, WatchEndReason.InvalidSession));
            await target!.QueuePacketAsync(new PacketWatchProducerStop(session.ID));
        }
    }

    private async Task HandleWatchResyncSnapshotResponseAsync(
        WatchResyncAttempt attempt,
        PacketWatchSnapshotResponse response
    )
    {
        List<(MiaoClientConnection Watcher, WatchSession Session)> recipients = new();
        WatchSceneSnapshot? snapshot = response.Snapshot;
        bool retry;

        using (stateLock.AcquireWriteLock())
        {
            if (!watchResyncCoordinator.TryFinishAttempt(
                attempt.TargetID,
                attempt.Generation
            ))
                return;

            MiaoClientConnection? target = serverState.Players.GetValueOrDefault(attempt.TargetID);
            bool validSnapshot = target is not null
                && response.IsSuccess
                && snapshot is not null
                && snapshot.Location == target.Player.Location
                && WatchPacketValidator.IsValid(snapshot);

            if (validSnapshot)
            {
                foreach (WatchSession session in watchSessions.GetByTarget(attempt.TargetID))
                {
                    if (!session.IsActive
                        || !session.IsResyncPending
                        || snapshot!.Location.Map != session.Map
                        || snapshot.Sequence < session.LastSequence)
                        continue;

                    session.CompleteResync(snapshot.Sequence);
                    if (serverState.Players.TryGetValue(session.WatcherID, out MiaoClientConnection? watcher))
                        recipients.Add((watcher, session));
                }
            }

            retry = HasPendingWatchResyncLocked(attempt.TargetID);
            if (!retry)
                watchResyncCoordinator.Complete(attempt.TargetID);
        }

        foreach ((MiaoClientConnection watcher, WatchSession session) in recipients)
        {
            await watcher.QueuePacketAsync(
                new PacketWatchResyncSnapshot(session.ID, attempt.TargetID, snapshot!)
            );
            logger.LogInformation(
                AppEvents.Watch,
                "Watch session {session} resynchronized at sequence {sequence}.",
                session.ID,
                snapshot!.Sequence
            );
        }

        if (retry)
            ScheduleWatchResyncRetry(attempt);
    }

    private Task HandleWatchResyncRequestFailureAsync(
        WatchResyncAttempt attempt,
        string reason
    )
    {
        bool retry = FinishFailedWatchResyncAttempt(attempt);
        logger.LogWarning(
            AppEvents.Watch,
            "Watch resync attempt {attempt} for target {target} {reason}.",
            attempt.Number,
            attempt.TargetID,
            reason
        );
        if (retry)
            ScheduleWatchResyncRetry(attempt);
        return Task.CompletedTask;
    }

    private bool FinishFailedWatchResyncAttempt(WatchResyncAttempt attempt)
    {
        using (stateLock.AcquireWriteLock())
        {
            if (!watchResyncCoordinator.TryFinishAttempt(
                attempt.TargetID,
                attempt.Generation
            ))
                return false;

            bool retry = HasPendingWatchResyncLocked(attempt.TargetID);
            if (!retry)
                watchResyncCoordinator.Complete(attempt.TargetID);
            return retry;
        }
    }

    private void ScheduleWatchResyncRetry(WatchResyncAttempt attempt)
    {
        bool scheduled;
        using (stateLock.AcquireWriteLock())
            scheduled = watchResyncCoordinator.TryScheduleRetry(attempt.TargetID);
        if (!scheduled)
            return;

        TimeSpan delay = attempt.Number == 1
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(2);
        _ = RetryWatchResyncAfterDelayAsync(attempt.TargetID, delay);
    }

    private async Task RetryWatchResyncAfterDelayAsync(int targetID, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            await RequestWatchResyncSnapshotAsync(targetID, scheduledRetry: true);
        }
        catch (Exception exception)
        {
            logger.LogError(
                AppEvents.Watch,
                exception,
                "Could not retry watch resynchronization for target {target}.",
                targetID
            );
        }
    }

    private bool HasPendingWatchResyncLocked(int targetID)
        => watchSessions.GetByTarget(targetID)
            .Any(session => session.IsActive && session.IsResyncPending);

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketWatchStop packet)
    {
        WatchSession? session;
        MiaoClientConnection? target = null;
        using (stateLock.AcquireWriteLock())
        {
            if (!watchSessions.TryGetByWatcher(connection.ID, out WatchSession? current)
                || current is null
                || current.ID != packet.SessionID
                || !watchSessions.Remove(current.ID, out session))
                return;

            serverState.Players.TryGetValue(session!.TargetID, out target);
        }

        logger.LogInformation(AppEvents.Watch, "Watch session {session} stopped by watcher {watcher}.", session!.ID, connection.ID);
        if (target is not null)
            await target.QueuePacketAsync(new PacketWatchProducerStop(session.ID));
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketWatchProducerStop packet)
    {
        WatchSession? session;
        MiaoClientConnection? watcher = null;
        using (stateLock.AcquireWriteLock())
        {
            if (!watchSessions.TryGet(packet.SessionID, out WatchSession? current)
                || current is null
                || current.TargetID != connection.ID
                || !watchSessions.Remove(current.ID, out session))
                return;

            serverState.Players.TryGetValue(session!.WatcherID, out watcher);
        }

        logger.LogInformation(
            AppEvents.Watch,
            "Watch session {session} stopped by producer {target}.",
            session!.ID,
            connection.ID
        );
        if (watcher is not null)
        {
            IContextualPacket notification = session.IsActive
                ? new PacketWatchEnded(session.ID, WatchEndReason.InvalidSession)
                : new PacketWatchStartResponse(WatchStartResult.TargetUnavailable, 0, null)
                {
                    RequestID = session.StartRequestID
                };
            await watcher.QueuePacketAsync(notification);
        }
    }

    private async Task EndWatchSessionsForPlayerAsync(
        MiaoClientConnection connection,
        WatchEndReason reason,
        bool connectionClosing
    )
    {
        List<(MiaoClientConnection Connection, IContextualPacket Packet)> notifications = new();
        int removedCount;

        using (stateLock.AcquireWriteLock())
            removedCount = RemoveWatchSessionsForPlayerLocked(
                connection,
                reason,
                connectionClosing,
                notifications
            );

        await SendWatchSessionEndNotificationsAsync(
            connection,
            reason,
            connectionClosing,
            removedCount,
            notifications
        );
    }

    private int RemoveWatchSessionsForPlayerLocked(
        MiaoClientConnection connection,
        WatchEndReason reason,
        bool connectionClosing,
        List<(MiaoClientConnection Connection, IContextualPacket Packet)> notifications
    )
    {
        IReadOnlyCollection<WatchSession> sessions = watchSessions.RemoveAllForPlayer(connection.ID);
        foreach (WatchSession session in sessions)
        {
            if ((!connectionClosing || session.TargetID != connection.ID)
                && serverState.Players.TryGetValue(session.TargetID, out MiaoClientConnection? target))
                notifications.Add((target, new PacketWatchProducerStop(session.ID)));

            if ((!connectionClosing || session.WatcherID != connection.ID)
                && serverState.Players.TryGetValue(session.WatcherID, out MiaoClientConnection? watcher))
            {
                IContextualPacket packet = session.IsActive
                    ? new PacketWatchEnded(session.ID, reason)
                    : new PacketWatchStartResponse(WatchStartResult.TargetUnavailable, 0, null)
                    {
                        RequestID = session.StartRequestID
                    };
                notifications.Add((watcher, packet));
            }
        }
        return sessions.Count;
    }

    private async Task SendWatchSessionEndNotificationsAsync(
        MiaoClientConnection connection,
        WatchEndReason reason,
        bool connectionClosing,
        int removedCount,
        List<(MiaoClientConnection Connection, IContextualPacket Packet)> notifications
    )
    {
        foreach ((MiaoClientConnection recipient, IContextualPacket packet) in notifications)
            await recipient.QueuePacketAsync(packet);

        if (removedCount > 0)
        {
            logger.LogInformation(
                AppEvents.Watch,
                "Removed {count} watch session(s) for player {player}: {reason}, closing={closing}.",
                removedCount,
                connection.ID,
                reason,
                connectionClosing
            );
        }
    }
}
