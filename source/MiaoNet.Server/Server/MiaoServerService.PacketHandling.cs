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
        r.Register<PacketPlayerMapChanged>(HandlePacketAsync);
        r.Register<PacketPlayerMapRoomChanged>(HandlePacketAsync);
        r.Register<PacketPlayerChannelMove>(HandlePacketAsync);
        r.Register<PacketChannelCreateAndJoin>(HandlePacketAsync);
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

        int fc = delta.FollowerInitials is not null
            ? delta.FollowerInitials.Length
            : delta.FollowerDeltas is not null
                ? delta.FollowerDeltas.Length
                : 0;
        if (fc > 12)
        {
            logger.LogWarning(AppEvents.Game, "Player {p} is taking up to {n} followers", player.Info, fc);
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
        await BroadcastContextuallyToAsync(
            new PacketContextualPlayerNotification<PacketPlayerFrame>(connection.ID, packet),
            u.Players,
            con => connection.ID != con.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        logger.LogDebug(
            AppEvents.GameState,
            "Player {p} map changing from {p1} to {p2}.",
            player.Info, player.Location, packet.Location
        );

        if (packet.Location.IsEmpty)
        {
            using (stateLock.AcquireWriteLock())
            {
                player.Channel.OnPlayerMapMove(connection, player.Location.Map, packet.Location.Map);

                player.Location = packet.Location;
                player.State = null;
            }
            // player went to menu or other non-Level places
            // just tell everyone about this thing
            await BroadcastContextuallyOthersAsync(
                new PacketPlayerMapChangedNotification(player.ID, PlayerLocation.Empty),
                connection.ID
            );
            return;
        }
        else if (packet.Location.IsInDebugMap)
        {
            using (stateLock.AcquireWriteLock())
            {
                player.Channel.OnPlayerMapMove(connection, player.Location.Map, packet.Location.Map);

                player.Location = packet.Location;
                player.State = null;
            }
            // player went to the debug map
            // tell everyone about this thing
            await BroadcastContextuallyOthersAsync(
                new PacketPlayerMapChangedNotification(player.ID, packet.Location),
                connection.ID
            );
            // TODO if the player went here from a different map then we should send states
            return;
        }

        // else, the player(A) went into a map
        // we need:
        // - tell players that in that map and in the same channel that someone comes
        // - tell other players that someone changed their location
        // - send detailed player states to A
        Debug.Assert(packet.Location.IsInMap);
        if (packet.InitialState is null)
        {
            logger.LogWarning(
                AppEvents.GameState,
                "Player {p} didn't send state when went to {loc}.",
                player.Info, packet.Location
            );
            await connection.DisconnectAsync(DisconnectReason.InvalidPacketWithState);
            return;
        }

        Task generalTask, withStateTask;
        ValueTask responseTask = default;

        using (stateLock.AcquireWriteLock())
        {
            var c = player.Channel;
            c.Maps.TryGetValue(packet.Location.Map, out var mapTo);

            mapTo?.StateLock.EnterWriteLock();
            try
            {
                var generalPacket = new PacketPlayerMapChangedNotification(player.ID, packet.Location);
                var withStatePacket = new PacketPlayerMapChangedNotification(player.ID, packet.Location, packet.InitialState);

                var mapPlayers = mapTo?.GetPlayerMovedInitialDatas(connection) ?? [];
                var responsePacket = new PacketPlayerMapChangedResponse(mapPlayers);

                generalTask = BroadcastContextuallyToAsync(
                    generalPacket,
                    mapTo is not null
                        ? c => !mapTo.Players.Contains(c) && c.ID != connection.ID
                        : c => c.ID != connection.ID
                );

                withStateTask = mapTo is not null ? BroadcastContextuallyToAsync(
                    withStatePacket,
                    mapTo.Players,
                    con => con.ID != connection.ID
                ) : Task.CompletedTask;
                responseTask = connection.QueuePacketAsync(responsePacket);

                c.OnPlayerMapMove(connection, player.Location.Map, packet.Location.Map);
                player.Location = packet.Location;
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

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerMapRoomChanged packet)
    {
        var player = connection.Player;
        logger.LogTrace(
            AppEvents.GameState,
            "Player {p} map room changed from room {p} to {a}.",
            player.Info, player.Location.Room,
            packet.MapRoom
        );
        player.Location = new(player.Location.Map, packet.MapRoom);
        await BroadcastOthersAsync(
            new PacketPlayerNotification<PacketPlayerMapRoomChanged>(player.ID, packet),
            connection.ID
        );
    }

    // TODO this has a large percent of "almost same" logic with `MapChanged` stuffs
    // and those can be shared
    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerChannelMove packet)
    {
        if (!serverState.Channels.TryGetValue(packet.TargetChannelID, out var channel))
        {
            // TODO tell the player
            return;
        }
        var player = connection.Player;

        if (player.Location.IsEmpty)
        {
            ValueTask responseTask;
            Task othersTask;
            using (stateLock.AcquireWriteLock())
            {
                var responsePacket = new PacketPlayerChannelMovedResponse(channel.ID, null);
                responseTask = connection.QueuePacketAsync(responsePacket);

                var othersPacket = new PacketPlayerChannelMovedNotification(player.ID, channel.ID);
                othersTask = BroadcastContextuallyOthersAsync(othersPacket, player.ID);

                serverState.PlayerChannelMove(connection, player.Channel, channel);
            }
            await responseTask;
            await othersTask;
        }
        else if (player.Location.IsInDebugMap)
        {
            ValueTask responseTask;
            Task othersTask;
            using (stateLock.AcquireWriteLock())
            {
                channel.Maps.TryGetValue(player.Location.Map, out ServerMap? mapTo);
                mapTo?.StateLock.EnterWriteLock();
                try
                {
                    var mapPlayers = mapTo?.GetPlayerMovedInitialDatas(connection);

                    var responsePacket = new PacketPlayerChannelMovedResponse(channel.ID, mapPlayers);
                    responseTask = connection.QueuePacketAsync(responsePacket);

                    var othersPacket = new PacketPlayerChannelMovedNotification(player.ID, channel.ID);
                    othersTask = BroadcastContextuallyOthersAsync(othersPacket, connection.ID);
                }
                finally
                {
                    mapTo?.StateLock.ExitWriteLock();
                }

                serverState.PlayerChannelMove(connection, player.Channel, channel);
            }
            await responseTask;
            await othersTask;
        }
        else
        {
            Debug.Assert(player.Location.IsInMap);

            ValueTask responseTask;
            Task nonSameMapTask;
            Task sameMapTask;

            using (stateLock.AcquireWriteLock())
            {
                channel.Maps.TryGetValue(player.Location.Map, out ServerMap? mapTo);
                mapTo?.StateLock.EnterWriteLock();
                try
                {
                    var mapPlayers = mapTo?.GetPlayerMovedInitialDatas(connection);

                    var responsePacket = new PacketPlayerChannelMovedResponse(channel.ID, mapPlayers);
                    responseTask = connection.QueuePacketAsync(responsePacket);

                    var nonSameMapNotification = new PacketPlayerChannelMovedNotification(connection.ID, channel.ID);
                    nonSameMapTask = BroadcastContextuallyToAsync(
                       nonSameMapNotification,
                       mapTo is not null
                            ? c => !mapTo.Players.Contains(c) && c.ID != connection.ID
                            : c => c.ID != connection.ID
                    );

                    var sameMapNotification = new PacketPlayerChannelMovedNotification(
                        connection.ID, channel.ID,
                        player.State
                    );
                    sameMapTask = mapTo is not null ? BroadcastContextuallyToAsync(
                        sameMapNotification,
                        mapTo.Players,
                        con => con.ID != connection.ID
                    ) : Task.CompletedTask;
                }
                finally
                {
                    mapTo?.StateLock.ExitWriteLock();
                }

                serverState.PlayerChannelMove(connection, player.Channel, channel);
            }

            await responseTask;
            await nonSameMapTask;
            await sameMapTask;
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketChannelCreateAndJoin packet)
    {
        Task createdTask;
        Task movedTask;
        ValueTask responseTask;

        using (stateLock.AcquireWriteLock())
        {
            var channel = serverState.CreateNewChannel(packet.ChannelInfo);
            serverState.AddChannel(channel);
            serverState.PlayerChannelMove(connection, connection.Player.Channel, channel);

            createdTask = BroadcastAsync(new PacketChannelCreated(channel.ID, channel.Info));
            movedTask = BroadcastContextuallyOthersAsync(
                new PacketPlayerChannelMovedNotification(connection.ID, channel.ID),
                connection.ID
            );
            responseTask = connection.QueuePacketAsync(new PacketPlayerChannelMovedResponse(channel.ID, null));
        }

        await createdTask;
        await movedTask;
        await responseTask;
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
            await BroadcastAsync(toSend);
            break;
        case ChatMessageType.ChannelChat:
            await BroadcastToAsync(toSend, connection.Player.Channel.Players, _ => true);
            break;
        case ChatMessageType.MapChat:
            await BroadcastToAsync(toSend, connection.Player.Channel.Players, c => c.Player.Location.Map == connection.Player.Location.Map);
            break;
        default:
            goto case ChatMessageType.Chat;
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendEmote packet)
    {
        await BroadcastToOthersAsync(
            new PacketEmote(connection.ID, packet.Emote),
            con => con.PlayerShouldSyncFrom(connection),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendEmoteText packet)
    {
        await BroadcastToOthersAsync(
            new PacketEmoteText(connection.ID, packet.Text),
            con => con.PlayerShouldSyncFrom(connection),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerLiveState packet)
    {
        await BroadcastToOthersAsync(
            new PacketPlayerNotification<PacketPlayerLiveState>(connection.ID, packet),
            con => con.PlayerShouldSyncFrom(connection),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketUpdateGlobalFlag packet)
    {
        connection.Player.GlobalFlags = packet.Flags;
        await BroadcastOthersAsync(
            new PacketPlayerNotification<PacketUpdateGlobalFlag>(connection.ID, packet),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketTeleportRequest request)
    {
        if (ServerState.Players.TryGetValue(request.TargetPlayerID, out var target))
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
        // TODO verify this action server-side
        PacketPlayerGrabPlayer send = packet.IsRelease ? new(connection.ID, packet.Force) : new(connection.ID);
        await p.QueuePacketAsync(send);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerGrabJumpOut packet)
    {
        if (!ServerState.Players.TryGetValue(packet.PlayerID, out var p))
            return;
        PacketPlayerGrabJumpOut send = new(connection.ID);
        await p.QueuePacketAsync(send);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerPlayedAudio packet)
    {
        var p = new PacketContextualPlayerNotification<PacketPlayerPlayedAudio>(connection.ID, packet);
        await BroadcastContextuallyToOthersAsync(p, c => c.PlayerShouldSyncFrom(connection), connection.ID);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketCreateFireworks packet)
    {
        if (connection.Player.TryConsumeFireworksToken())
        {
            PacketPlayerNotification<PacketCreateFireworks> notification = new(connection.ID, packet);
            await BroadcastToOthersAsync(notification, c => c.PlayerShouldSyncFrom(connection), connection.ID);
        }
        else
        {
            // TODO localization
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Too many fireworks.");
        }
    }
}