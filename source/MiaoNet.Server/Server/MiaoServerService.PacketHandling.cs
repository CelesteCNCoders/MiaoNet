using Microsoft.Extensions.Logging;
using MiaoNet.Shared;
using System.Diagnostics;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // private ServerChannel GetChannel(ServerPlayer player)
    //     => serverState.Channels[player.ChannelId];

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

        var mapScope = serverState.ScopeTree.MapOf(player);
        if (mapScope is null) return;

        await mapScope.PostAsync(() =>
        {
            player.State.ApplyDelta(delta);

            BroadcastContextuallyToAsync(
                new PacketContextualPlayerNotification<PacketPlayerFrame>(connection.ID, packet),
                mapScope.Players.Select(p => p.Connection!),
                con => connection.ID != con.ID
            );
            return Task.CompletedTask;
        });
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
            // No location meaning quit map and goes back to channel
            var emptyChannelScope = serverState.ScopeTree.ChannelOf(player);
            if (emptyChannelScope is not null)
                serverState.ScopeTree.MovePlayer(player, emptyChannelScope);

            player.Location = packet.Location;
            player.State = null;

            await BroadcastContextuallyOthersAsync(
                new PacketPlayerMapChangedNotification(player.ID, PlayerLocation.Empty),
                connection.ID
            );
            return;
        }
        else if (packet.Location.IsInDebugMap)
        {
            // Debug map still needs a MapScope, but no state sync
            player.Location = packet.Location;
            player.State = null;

            serverState.MovePlayerToMap(player, packet.Location.Map);

            await BroadcastContextuallyOthersAsync(
                new PacketPlayerMapChangedNotification(player.ID, packet.Location),
                connection.ID
            );
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

        // Move player to the new map scope
        player.Location = packet.Location;
        player.State = packet.InitialState;

        var moveResult = serverState.MovePlayerToMap(player, packet.Location.Map);
        var targetMapScope = serverState.ScopeTree.MapOf(player)!;

        {
            // Snapshot inside the map's consumer task
            var mapPlayers = await targetMapScope.PostAsync(() =>
                targetMapScope.Players
                    .Where(p => p != player)
                    .Select(p => new PlayerMovedInitialData(p.ID, p.State!.Clone()))
                    .ToList()
            );

            var responsePacket = new PacketPlayerMapChangedResponse(mapPlayers);
            responseTask = connection.QueuePacketAsync(responsePacket);

            // Tell new map peers (full state)
            var withStatePacket = new PacketPlayerMapChangedNotification(player.ID, packet.Location, packet.InitialState);
            withStateTask = BroadcastContextuallyToAsync(
                withStatePacket,
                moveResult.NewPeers.Select(p => p.Connection!),
                con => con.ID != connection.ID
            );

            // Tell everyone else (location only, exclude new map peers who already got full state)
            var generalPacket = new PacketPlayerMapChangedNotification(player.ID, packet.Location);
            generalTask = BroadcastContextuallyToAsync(
                generalPacket,
                serverState.Players.Values,
                c => c.ID != connection.ID && !moveResult.NewPeers.Contains(c.Player)
            );
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
        logger.LogDebug(AppEvents.GameState,
            "ChannelMove: {player} to channel {ch}, location={loc}, scope={scope}",
            player.Info, channel.Info.Name, player.Location, player.Scope);

        if (player.Location.IsEmpty)
        {
            serverState.MovePlayerToChannel(player, channel);

            var responsePacket = new PacketPlayerChannelMovedResponse(channel.ID, null);
            var responseTask = connection.QueuePacketAsync(responsePacket);

            var othersPacket = new PacketPlayerChannelMovedNotification(player.ID, channel.ID);
            var othersTask = BroadcastContextuallyOthersAsync(othersPacket, player.ID);

            await responseTask;
            await othersTask;
        }
        else if (player.Location.IsInDebugMap)
        {
            serverState.MovePlayerToChannel(player, channel);
            var mapScope = serverState.ScopeTree.MapOf(player);

            IReadOnlyCollection<PlayerMovedInitialData>? mapPlayers = null;
            if (mapScope is not null)
            {
                mapPlayers = await mapScope.PostAsync(() =>
                    mapScope.Players
                        .Where(p => p != player)
                        .Select(p => new PlayerMovedInitialData(p.ID, p.State!.Clone()))
                        .ToList()
                );
            }

            var responsePacket = new PacketPlayerChannelMovedResponse(channel.ID, mapPlayers);
            var responseTask = connection.QueuePacketAsync(responsePacket);

            var othersPacket = new PacketPlayerChannelMovedNotification(player.ID, channel.ID);
            var othersTask = BroadcastContextuallyOthersAsync(othersPacket, connection.ID);

            await responseTask;
            await othersTask;
        }
        else
        {
            Debug.Assert(player.Location.IsInMap);

            var moveResult = serverState.MovePlayerToChannel(player, channel);
            var mapScope = serverState.ScopeTree.MapOf(player);

            logger.LogDebug(AppEvents.GameState,
                "ChannelMove IsInMap: newPeers={new}, prevPeers={prev}, mapScope={scope}",
                moveResult.NewPeers.Count, moveResult.PreviousPeers.Count, mapScope);

            IReadOnlyCollection<PlayerMovedInitialData>? mapPlayers = null;
            if (mapScope is not null)
            {
                mapPlayers = await mapScope.PostAsync(() =>
                    mapScope.Players
                        .Where(p => p != player)
                        .Select(p => new PlayerMovedInitialData(p.ID, p.State!.Clone()))
                        .ToList()
                );
            }

            var responsePacket = new PacketPlayerChannelMovedResponse(channel.ID, mapPlayers);
            var responseTask = connection.QueuePacketAsync(responsePacket);

            var nonSameMapNotification = new PacketPlayerChannelMovedNotification(connection.ID, channel.ID);
            var nonSameMapTask = BroadcastContextuallyToAsync(
                nonSameMapNotification,
                serverState.Players.Values,
                c => c.ID != connection.ID && !moveResult.NewPeers.Contains(c.Player)
            );

            Task sameMapTask = Task.CompletedTask;
            if (mapScope is not null && moveResult.NewPeers.Count > 0)
            {
                var sameMapNotification = new PacketPlayerChannelMovedNotification(
                    connection.ID, channel.ID,
                    player.State
                );
                sameMapTask = BroadcastContextuallyToAsync(
                    sameMapNotification,
                    moveResult.NewPeers.Select(p => p.Connection!),
                    con => con.ID != connection.ID
                );
            }

            await responseTask;
            await nonSameMapTask;
            await sameMapTask;
        }
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketChannelCreateAndJoin packet)
    {
        var channel = serverState.CreateChannel(packet.ChannelInfo);
        serverState.MovePlayerToChannel(connection.Player, channel);

        var createdTask = BroadcastAsync(new PacketChannelCreated(channel.ID, channel.Info));
        var movedTask = BroadcastContextuallyOthersAsync(
            new PacketPlayerChannelMovedNotification(connection.ID, channel.ID),
            connection.ID
        );
        var responseTask = connection.QueuePacketAsync(new PacketPlayerChannelMovedResponse(channel.ID, null));

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
            var channelScope = serverState.ScopeTree.ChannelOf(connection.Player);
            if (channelScope is not null)
                await BroadcastToAsync(toSend, channelScope.AllPlayers.Select(p => p.Connection!), _ => true);
            break;
        case ChatMessageType.MapChat:
            var chatMapScope = serverState.ScopeTree.MapOf(connection.Player);
            if (chatMapScope is not null)
                await BroadcastToAsync(toSend, chatMapScope.Players.Select(p => p.Connection!), _ => true);
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