using System.Buffers;
using System.Diagnostics;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    private void RegisterPacketHandlers(PacketHandlerRegister r)
    {
        r.Register<PacketPlayerFrame>(HandlePacketAsync);
        r.Register<PacketPlayerLocationChanged>(HandlePacketAsync);
        r.Register<PacketPlayerChannelMove>(HandlePacketAsync);
        r.Register<PacketSendChatMessage>(HandlePacketAsync);
        r.Register<PacketEmote>(HandlePacketAsync);
        r.Register<PacketEmoteText>(HandlePacketAsync);
        r.Register<PacketPlayerLiveState>(HandlePacketAsync);
        r.Register<PacketUpdateGlobalFlag>(HandlePacketAsync);
        r.Register<PacketTeleportRequest>(HandlePacketAsync);
        r.Register<PacketSendPrivateChatMessage>(HandlePacketAsync);
        r.Register<PacketPlayerPlayedAudio>(HandlePacketAsync);
        r.Register<PacketPlayerGrabPlayer>(HandlePacketAsync);
        r.Register<PacketPlayerGrabJumpOut>(HandlePacketAsync);
        r.Register<PacketCreateFireworks>(HandlePacketAsync);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerFrame packet)
    {
        var player = connection.Player;
        if (player.State is null)
        {
            logger.LogError(AppEvents.Game, "Received a player frame from {p} but there is no initial state.", player.Info);
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }
        else if (!player.Location.IsInMap)
        {
            logger.LogError(AppEvents.Game, "Player {p} is not in a map but sent a player frame packet.", player.Info);
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

        // TODO we can actually using one Task for one Map
        // to handle these updates lock-free
        ServerMap u = player.Channel.Maps[player.Location.Map];
        using (u.StateLock.AcquireReadLock())
        {
            var state = player.State;
            state.ApplyDelta(delta);
        }
        await BroadcastToScopeExceptAsync(
            packet,
            u,
            connection.ID,
            envelope: PacketEnvelope.FromSender(connection.ID)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerLocationChanged packet)
    {
        var player = connection.Player;
        var oldLocation = player.Location;
        var newLocation = packet.Location;
        logger.LogDebug(
            AppEvents.GameState,
            "Player {p} is changing location from {p1} to {p2}.",
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
                    new PacketPlayerLocationChangedNotification(newLocation, null),
                    player.Channel,
                    connection.ID,
                    envelope: PacketEnvelope.FromSender(player.ID)
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
                new PacketPlayerLocationChangedNotification(newLocation, null),
                player.Channel,
                connection.ID,
                envelope: PacketEnvelope.FromSender(player.ID)
            );
            return;
        }

        // now the initial state is necessary
        // note that map reentering is supported, so "oldLocation.Map == newLocation.Map" can be true here
        if (packet.InitialState is null)
        {
            logger.LogWarning(
                AppEvents.GameState,
                "Player {p} didn't send its state when moving to {loc}.",
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
                var generalPacket = new PacketPlayerLocationChangedNotification(newLocation, null);
                var withStatePacket = new PacketPlayerLocationChangedNotification(newLocation, packet.InitialState);

                var mapPlayers = mapTo?.GetPlayerMovedInitialDatas(connection) ?? [];
                var responsePacket = new PacketPlayerLocationChangedResponse(mapPlayers);

                PacketEnvelope playerEnvelope = PacketEnvelope.FromSender(player.ID);

                generalTask = mapTo is not null
                    ? BroadcastToScopeExceptAsync(generalPacket, player.Channel, connection.ID, c => !mapTo.Players.Contains(c), playerEnvelope)
                    : BroadcastToScopeExceptAsync(generalPacket, player.Channel, connection.ID, playerEnvelope);

                withStateTask = mapTo is not null
                    ? BroadcastToScopeExceptAsync(withStatePacket, mapTo, connection.ID, playerEnvelope)
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

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerChannelMove packet)
    {
        var player = connection.Player;

        logger.LogInformation(
            AppEvents.Channel,
            "{player} is moving from channel \"{from}\" to \"{to}\".",
            player,
            player.Channel.Info.Name,
            packet.TargetChannelName
        );

        ValueTask responseTask;
        Task sameMapTask;
        Task sameChannelTask;
        Task crossChannelTask;
        Task createdBroadcastTask = Task.CompletedTask;
        ValueTask createdTask = default;
        bool notifyChannelCreated = false;

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
                    targetChannel.ID,
                    player.State is null ? null : new PlayerMovedInitialData(player.State),
                    new PlayerPresenceData(player.Location, player.GlobalFlags)
                );
                PacketEnvelope playerEnvelope = PacketEnvelope.FromSender(connection.ID);
                sameMapTask = mapTo is not null
                    ? BroadcastToScopeExceptAsync(sameMapNotification, mapTo, connection.ID, playerEnvelope)
                    : Task.CompletedTask;

                var sameChannelNotification = new PacketPlayerChannelMovedNotification(
                    targetChannel.ID,
                    null,
                    new PlayerPresenceData(player.Location, player.GlobalFlags)
                );
                sameChannelTask = BroadcastToScopeExceptAsync(
                    sameChannelNotification,
                    targetChannel,
                    connection.ID,
                    c => mapTo is null || !mapTo.Players.Contains(c),
                    playerEnvelope
                );

                // players in other channels get only a "moved" notification
                // and for private channels, the virtual id is used instead of the real channel id
                var crossChannelNotification = new PacketPlayerChannelMovedNotification(
                    targetChannel.IsPrivate ? ChannelInfo.PrivateChannelVirtualID : targetChannel.ID
                );
                crossChannelTask = BroadcastToScopeExceptAsync(
                    crossChannelNotification,
                    serverState,
                    connection.ID,
                    c => c.Player.Channel != targetChannel,
                    playerEnvelope
                );
            }
            finally
            {
                mapTo?.StateLock.ExitWriteLock();
            }

            serverState.PlayerChannelMove(connection, player.Channel, targetChannel);
        }

        if (notifyChannelCreated)
            await createdTask;
        await createdBroadcastTask;
        await responseTask;
        await sameMapTask;
        await sameChannelTask;
        await crossChannelTask;
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketSendChatMessage packet)
    {
        logger.LogInformation(AppEvents.GameChat, "[{channel}] {player}: {msg}", packet.ChatChannel, connection.Player.Info, packet.Content);
        if (packet.Content.Length > 64)
        {
            logger.LogWarning(AppEvents.GameChat, "{player} sent an oversized chat message.", connection.Player.Info);
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

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketEmote packet)
    {
        await BroadcastToScopeExceptAsync(
            packet,
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection),
            PacketEnvelope.FromSender(connection.ID)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketEmoteText packet)
    {
        await BroadcastToScopeExceptAsync(
            packet,
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection),
            PacketEnvelope.FromSender(connection.ID)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerLiveState packet)
    {
        await BroadcastToScopeExceptAsync(
            packet,
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection),
            PacketEnvelope.FromSender(connection.ID)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketUpdateGlobalFlag packet)
    {
        connection.Player.GlobalFlags = packet.Flags;
        await BroadcastToScopeExceptAsync(
            packet,
            connection.Player.Channel,
            connection.ID,
            PacketEnvelope.FromSender(connection.ID)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketTeleportRequest request)
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
                    envelope,
                    new PacketTeleportResponse(PacketTeleportResponse.TeleportFailedReason.OtherDoesNotResponse, null)
                );
            }

            Task OnOtherResponse(PacketBeTeleportedResponse response)
            {
                if (response.Accepted)
                {
                    logger.LogInformation(AppEvents.Game, "{p}'s teleport request to {p2} was accepted.", connection.Player.Info, target.Player.Info);
                    return connection.ResponseAsync(
                        envelope,
                        new PacketTeleportResponse(PacketTeleportResponse.TeleportFailedReason.None, response.Session)
                    ).AsTask();
                }
                else
                {
                    logger.LogInformation(AppEvents.Game, "{p}'s teleport request to {p2} was rejected.", connection.Player.Info, target.Player.Info);
                    return connection.ResponseAsync(
                        envelope,
                        new PacketTeleportResponse(PacketTeleportResponse.TeleportFailedReason.OtherDenied, null)
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
                    envelope,
                    new PacketTeleportResponse(PacketTeleportResponse.TeleportFailedReason.OtherDoesNotResponse, null)
                ).AsTask();
            }
        }
        else
        {
            logger.LogInformation(
                AppEvents.Game,
                "{p} is requesting to teleport to player (id {id}), which does not exist.",
                connection.Player.Info,
                request.TargetPlayerID
            );
            await connection.ResponseAsync(
                envelope,
                new PacketTeleportResponse(PacketTeleportResponse.TeleportFailedReason.NoSuchPlayer, null)
            );
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketSendPrivateChatMessage request)
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
            await connection.ResponseAsync(envelope, new PacketSendPrivateChatMessageResponse(DateTime.UtcNow, PacketSendPrivateChatMessageResponse.SendResult.Success));
        }
        else
        {
            logger.LogInformation(
                AppEvents.GameChat,
                "{player} tried to send a private message to player (id {id}), which does not exist.",
                connection.Player.Info,
                request.TargetPlayerID
            );
            await connection.ResponseAsync(
                envelope,
                new PacketSendPrivateChatMessageResponse(DateTime.UtcNow, PacketSendPrivateChatMessageResponse.SendResult.NoSuchPlayer)
            );
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerGrabPlayer packet)
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

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerGrabJumpOut packet)
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

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketPlayerPlayedAudio packet)
    {
        await BroadcastToScopeExceptAsync(
            packet,
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection),
            PacketEnvelope.FromSender(connection.ID)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketCreateFireworks packet)
    {
        if (connection.Player.TryConsumeFireworksToken())
        {
            await BroadcastToScopeExceptAsync(
                packet,
                serverState,
                connection.ID,
                c => c.PlayerShouldSyncFrom(connection),
                PacketEnvelope.FromSender(connection.ID)
            );
        }
        else
        {
            // TODO localization
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Too many fireworks.");
        }
    }
}
