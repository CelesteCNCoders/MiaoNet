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

        // TODO we can actually using one Task for one Map
        // to handle these updates lock-free
        ServerMap u = player.Channel.Maps[player.Location.Map];
        using (u.StateLock.AcquireReadLock())
        {
            var state = player.State;
            state.ApplyDelta(delta);
        }
        await BroadcastToScopeExceptAsync(
            new PacketContextualPlayerNotification<PacketPlayerFrame>(connection.ID, packet),
            u,
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerLocationChanged packet)
    {
        var player = connection.Player;
        var oldLocation = player.Location;
        var newLocation = packet.Location;
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
        await BroadcastToScopeExceptAsync(
            new PacketPlayerNotification<PacketPlayerLiveState>(connection.ID, packet),
            serverState,
            connection.ID,
            c => c.PlayerShouldSyncFrom(connection)
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketUpdateGlobalFlag packet)
    {
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
            await target.RequestAsync(new PacketBeTeleportedRequest(connection.ID), OnOtherResponse);

            // TODO timeout
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
        // holding requires the same channel and the same map
        if (p.Player.Channel != connection.Player.Channel
            || p.Player.Location.Map != connection.Player.Location.Map)
            return;
        // TODO verify this action server-side
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
}
