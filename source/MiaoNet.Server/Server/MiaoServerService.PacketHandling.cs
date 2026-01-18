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
        r.Register<PacketSendChatMessage>(HandlePacketAsync);
        r.Register<PacketSendEmote>(HandlePacketAsync);
        r.Register<PacketSendEmoteText>(HandlePacketAsync);
        r.Register<PacketPlayerStateFlags>(HandlePacketAsync);
        r.Register<PacketUpdateOnlineStatus>(HandlePacketAsync);
        r.Register<PacketTeleportRequest>(HandlePacketAsync);
        r.Register<PacketSendPrivateChatMessage>(HandlePacketAsync);
        r.Register<PacketPlayerGrabPlayer>(HandlePacketAsync);
        r.Register<PacketPlayerGrabJumpOut>(HandlePacketAsync);
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

        var state = player.State;
        state.FacingLeft = packet.FacingLeft;
        state.Position = packet.Position;
        if (packet.DashesChange)
            state.Dashes = packet.Dashes;
        if (packet.HasFollowerInitials)
            state.ApplyFollowersInitials(packet.FollowerInitials);
        else if (packet.HasFollowerDeltas)
            state.ApplyFollowersDeltas(packet.FollowerDeltas);
        if (packet.HasWindDirection)
            state.WindDirection = packet.WindDirection;
        if (packet.HasHoldable)
            state.ApplyHoldableInfo(packet.HoldableInfo);

        await BroadcastContextuallyToOthersAsync(
            new PacketContextualPlayerNotification<PacketPlayerFrame>(connection.ID, packet),
            player.Channel,
            con => con.Player.ShouldSyncFrom(player),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        player.State = packet.InitialState;
        logger.LogDebug(
            AppEvents.GameState,
            "Player {p} map changing from {p1} to {p2}.",
            player.Info, player.Location, packet.Location
        );

        serverState.StateLock.EnterWriteLock();
        player.Location = packet.Location;
        serverState.StateLock.ExitWriteLock();

        if (packet.Location.IsEmpty)
        {
            // player went to menu or other non-Level places
            // just tell everyone about this thing
            Task task;
            serverState.StateLock.EnterReadLock();
            try
            {
                task = BroadcastContextuallyOthersAsync(
                    new PacketPlayerMapChangedNotification(player.ID, PlayerLocation.Empty),
                    connection.ID
                );
            }
            finally
            {
                serverState.StateLock.ExitReadLock();
            }
            await task;
            return;
        }
        else if (packet.Location.IsInDebugMap)
        {
            // player went to the debug map
            // tell everyone about this thing
            Task task;
            serverState.StateLock.EnterReadLock();
            try
            {
                task = BroadcastContextuallyOthersAsync(
                    new PacketPlayerMapChangedNotification(player.ID, packet.Location),
                    connection.ID
                );
            }
            finally
            {
                serverState.StateLock.ExitReadLock();
            }
            await task;
            // and seems there're no other things to do...
            // since if the player went to the debug map
            // then they must be in the corresponding map previously
            // that the client has states of them
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

        serverState.StateLock.EnterReadLock();
        Task generalTask, withStateTask;
        ValueTask responseTask = default;

        try
        {
            var generalPacket = new PacketPlayerMapChangedNotification(
                player.ID, packet.Location
            );
            var withStatePacket = new PacketPlayerMapChangedNotification(
                player.ID, packet.Location,
                null, packet.InitialState
            );
            var mapPlayers =
                from pair in connection.Player.Channel.Players
                where player.ShouldSyncFrom(pair.Value.Player)
                where pair.Value.Connection != connection
                select new PacketPlayerMapChangedResponse.Player(
                    pair.Key,
                    pair.Value.Player.State!, // TODO check if it's null (then kick them :L)
                    pair.Value.Player.GraphicsInfo
                );
            var responsePacket = new PacketPlayerMapChangedResponse(mapPlayers.ToArray());

            generalTask = BroadcastContextuallyToOthersAsync(
                generalPacket,
                c => !c.Player.ShouldSyncFrom(player),
                player.ID
            );

            withStateTask = BroadcastContextuallyToOthersAsync(
                withStatePacket,
                connection.Player.Channel,
                c => c.Player.ShouldSyncFrom(player),
                player.ID
            );
            responseTask = connection.QueuePacketAsync(responsePacket);
        }
        finally
        {
            serverState.StateLock.ExitReadLock();
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
            player.Info, player.Location.MapRoom,
            packet.MapRoom
        );
        serverState.StateLock.EnterWriteLock();
        player.Location.MapRoom = packet.MapRoom;
        serverState.StateLock.ExitWriteLock();
        await BroadcastOthersAsync(new PacketPlayerNotification<PacketPlayerMapRoomChanged>(player.ID, packet), connection.ID);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketSendChatMessage packet)
    {
        logger.LogInformation(AppEvents.GameChat, "{player}: {msg}", connection.Player.Info, packet.Content);
        await BroadcastAsync(new PacketChatMessage(DateTime.UtcNow, ChatMessageType.Chat, connection.Player.ID, packet.Content));
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

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerStateFlags packet)
    {
        await BroadcastToOthersAsync(
            new PacketPlayerNotification<PacketPlayerStateFlags>(connection.ID, packet),
            con => con.PlayerShouldSyncFrom(connection),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketUpdateOnlineStatus packet)
    {
        connection.Player.OnlineStatus = packet.Status;
        await BroadcastOthersAsync(
            new PacketPlayerNotification<PacketUpdateOnlineStatus>(connection.ID, packet),
            connection.ID
        );
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketTeleportRequest request)
    {
        if (ServerState.AllPlayers.TryGetValue(request.TargetPlayerID, out var target))
        {
            logger.LogInformation(AppEvents.Game, "{p} is requesting to teleport to {p2}.", connection.Player.Info, target.Player.Info);
            await target.Connection.RequestAsync(new PacketBeTeleportedRequest(connection.ID), OnOtherResponse);

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
        if (ServerState.AllPlayers.TryGetValue(request.TargetPlayerID, out var target))
        {
            logger.LogInformation(
                AppEvents.GameChat,
                "{player} -> {target}: {msg}",
                connection.Player.Info,
                target.Player.Info,
                request.Content
             );

            await target.Connection.QueuePacketAsync(
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
        if (!ServerState.AllPlayers.TryGetValue(packet.PlayerID, out var p))
            return;
        if (p.Player.OnlineStatus != PlayerOnlineStatus.Normal)
            return;
        PacketPlayerGrabPlayer send = packet.IsRelease ? new(connection.ID, packet.Force) : new(connection.ID);
        await p.Connection.QueuePacketAsync(send);
    }

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketPlayerGrabJumpOut packet)
    {
        if (!ServerState.AllPlayers.TryGetValue(packet.PlayerID, out var p))
            return;
        PacketPlayerGrabJumpOut send = new(connection.ID);
        await p.Connection.QueuePacketAsync(send);
    }
}