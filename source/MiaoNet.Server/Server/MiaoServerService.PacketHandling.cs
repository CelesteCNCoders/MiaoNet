using Microsoft.Extensions.Logging;
using MiaoNet.Shared;
using System.Diagnostics;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    private void RegisterPacketHandlers(PacketHandlerRegister register)
    {
        register.Register<PacketPlayerFrame>(HandlePacket);
        register.Register<PacketPlayerMapChanged>(HandlePacket);
        register.Register<PacketPlayerMapRoomChanged>(HandlePacket);
        register.Register<PacketSendChatMessage>(HandlePacket);
    }

    private async Task HandlePacket(MiaoClientConnection connection, PacketPlayerFrame packet)
    {
        var player = connection.Player;
        if (player.State is null)
        {
            logger.LogError(AppEvents.Game, "Packet frame received but no initial state for {p}.", player.Info);
            connection.Disconnect(KickedReason.InvalidPacketWithState);
            return;
        }
        else if (!player.Location.IsInMap)
        {
            logger.LogError(AppEvents.Game, "Player {p} is not in map but sent PacketPlayerFrame!", player.Info);
            connection.Disconnect(KickedReason.InvalidPacketWithState);
            return;
        }

        var state = player.State;
        state.Position = packet.Position;
        if (packet.DashesChange)
            state.Dashes = packet.Dashes;

        await BroadcastToOthersAsync(
            new PacketPlayerFrameNotification(connection.ID, packet),
            player.Channel,
            con => con.Player.ShouldSyncFrom(player),
            connection.ID
        );
    }

    private async Task HandlePacket(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        player.State = packet.InitialState;
        logger.LogDebug(
            "Player {p} map changed from {p1} to {p2}.",
            player.Info, player.Location, packet.Location
        );

        serverState.StateLock.EnterWriteLock();
        player.Location = packet.Location;
        serverState.StateLock.ExitWriteLock();

        if (packet.Location.IsEmpty)
        {
            // player went to menu or other non-Level places
            // just tell everyone about this thing
            await BroadcastOthersAsync(new PacketPlayerMapChangedNotification(player.ID, PlayerLocation.Empty), connection.ID);
            return;
        }
        else if (packet.Location.IsInDebugMap)
        {
            // player went to the debug map
            // tell everyone about this thing
            await BroadcastOthersAsync(new PacketPlayerMapChangedNotification(player.ID, packet.Location), connection.ID);
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
            logger.LogWarning("Player {p} didn't send state when went to {loc}.", player.Info, packet.Location);
            connection.Disconnect(KickedReason.InvalidPacketWithState);
            return;
        }

        serverState.StateLock.EnterReadLock();
        Task generalTask, withStateTask;
        ValueTask responseTask = default;

        try
        {
            IPacket generalPacket = new PacketPlayerMapChangedNotification(
                player.ID, packet.Location
            );
            IPacket withStatePacket = new PacketPlayerMapChangedNotification(
                player.ID, packet.Location,
                null, packet.InitialState
            );
            var mapPlayers =
                from pair in connection.Player.Channel.Players
                where player.Location.IsSameMapWith(pair.Value.Player.Location)
                where pair.Value.Connection != connection
                select new PacketPlayerMapChangedResponse.Player(
                    pair.Key,
                    pair.Value.Player.State!, // TODO check if it's null (then kick them :L)
                    pair.Value.Player.GraphicsInfo
                );
            IPacket responsePacket = new PacketPlayerMapChangedResponse(mapPlayers.ToArray());

            generalTask = BroadcastToOthersAsync(
                generalPacket,
                c => !c.Player.ShouldSyncFrom(player),
                player.ID
            );
            withStateTask = BroadcastToOthersAsync(
                withStatePacket,
                c => c.Player.ShouldSyncFrom(player),
                player.ID
            );
            responseTask = connection.SendPacketAsync(responsePacket);
        }
        finally
        {
            serverState.StateLock.ExitReadLock();
        }

        await generalTask;
        await withStateTask;
        await responseTask;
    }

    private async Task HandlePacket(MiaoClientConnection connection, PacketPlayerMapRoomChanged packet)
    {
        var player = connection.Player;
        logger.LogDebug(
            AppEvents.GameState,
            "Player {p} map room changed from room {p} to {a}.",
            player.Info, player.Location.MapRoom,
            packet.MapRoom
        );
        serverState.StateLock.EnterWriteLock();
        player.Location.MapRoom = packet.MapRoom;
        serverState.StateLock.ExitWriteLock();
        await BroadcastOthersAsync(new PacketPlayerMapRoomChangedNotification(player.ID, packet), connection.ID);
    }

    private async Task HandlePacket(MiaoClientConnection connection, PacketSendChatMessage packet)
    {
        logger.LogInformation(AppEvents.GameChat, "{player}: {msg}", connection.Player.Info, packet.Content);
        await BroadcastAsync(new PacketChatMessage(ChatMessageType.Chat, connection.Player.ID, packet.Content));
    }
}