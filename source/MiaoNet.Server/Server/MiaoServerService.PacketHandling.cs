using Microsoft.Extensions.Logging;
using MiaoNet.Shared;

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
        if (connection.Player.State is null)
        {
            logger.LogError(AppEvents.Game, "Packet frame received but no initial state for {p}.", connection.Player.Info);
            connection.Disconnect(KickedReason.InvalidPacketWithState);
            return;
        }
        else if (connection.Player.Location.IsEmpty)
        {
            logger.LogError(AppEvents.Game, "Player {p} is in Empty location but sent PacketPlayerFrame!", connection.Player.Info);
            connection.Disconnect(KickedReason.InvalidPacketWithState);
            return;
        }
        else
        {
            var state = connection.Player.State;
            state.X = packet.X;
            state.Y = packet.Y;
            if (packet.DashesChange)
                state.Dashes = packet.Dashes;
        }
        await BroadcastToOthersAsync(
            new PacketPlayerFrameNotification(connection.ID, packet),
            con => con.Player.Location.SameMapWith(connection.Player.Location),
            connection
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

        serverState.StateLock.EnterReadLock();
        Task normalTask, sameMapTask;
        ValueTask changerTask;
        // TODO channels

        try
        {
            IPacket normal = new PacketPlayerMapChangedNotification(
                player.Info.ID, packet.Location
            );
            IPacket sameMap = new PacketPlayerMapChangedNotification(
                player.Info.ID, packet.Location,
                null, packet.InitialState
            );
            IPacket changer = new PacketPlayerMapChangedResponse(
                serverState.AllPlayers.Where(p => SameMapPredicate(p.Value.Player))
                    .Where(p => p.Value.Connection != connection)
                    .Select(p => new PacketPlayerMapChangedResponse.Player(
                        p.Key,
                        p.Value.Player.State!, // TODO
                        p.Value.Player.GraphicsInfo
                    )
                ).ToArray()
            );

            normalTask = BroadcastToOthersAsync(normal, c => NormalPredicate(c.Player), player.ID);
            sameMapTask = BroadcastToOthersAsync(sameMap, c => SameMapPredicate(c.Player), player.ID);
            changerTask = connection.SendPacketAsync(changer);

            bool NormalPredicate(ServerPlayer other)
                => other.Location.IsEmpty || !other.Location.SameMapWith(player.Location);

            bool SameMapPredicate(ServerPlayer other)
                => !NormalPredicate(other);
        }
        finally
        {
            serverState.StateLock.ExitReadLock();
        }

        await changerTask;
        await normalTask;
        await sameMapTask;
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
        await BroadcastOthersAsync(new PacketPlayerMapRoomChangedNotification(player.ID, packet), connection);
    }

    private async Task HandlePacket(MiaoClientConnection connection, PacketSendChatMessage packet)
    {
        logger.LogInformation(AppEvents.GameChat, "{player}: {msg}", connection.Player.Info, packet.Content);
        await BroadcastAsync(new PacketChatMessage(ChatMessageType.Chat, connection.Player.ID, packet.Content));
    }
}