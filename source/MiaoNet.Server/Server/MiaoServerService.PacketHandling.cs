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
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerFrame packet)
    {
        if (connection.Player.State is null)
        {
            logger.LogError(AppEvents.Game, "Packet frame received but no initial state for {p}.", connection.Player.Info);
            connection.Disconnect();
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
            con => con.Player.LocationInfo.MapSid == connection.Player.LocationInfo.MapSid,
            connection
        );
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        var playerLoc = player.LocationInfo;
        player.State = packet.InitialState;
        logger.LogDebug(
            "{p} map changed: from {p} to {n.s}.{n.r}.",
            player.Info, playerLoc,
            packet.MapSid, packet.MapRoom
        );

        serverState.StateLock.EnterWriteLock();
        playerLoc.MapSid = packet.MapSid;
        playerLoc.MapRoom = packet.MapRoom;
        serverState.StateLock.ExitWriteLock();

        serverState.StateLock.EnterReadLock();
        Task normalTask, sameMapTask;
        ValueTask changerTask;
        // TODO channels

        try
        {
            IPacket normal = new PacketPlayerMapChangedNotification(
                player.Info.ID,
                playerLoc.MapSid, playerLoc.MapRoom
            );
            IPacket sameMap = new PacketPlayerMapChangedNotification(
                player.Info.ID,
                playerLoc.MapSid, playerLoc.MapRoom,
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
                ).ToList()
            );

            normalTask = BroadcastToOthersAsync(normal, c => NormalPredicate(c.Player), player.ID);
            sameMapTask = BroadcastToOthersAsync(sameMap, c => SameMapPredicate(c.Player), player.ID);
            changerTask = connection.SendPacketAsync(changer);

            bool NormalPredicate(ServerPlayer player)
            {
                string sid = player.LocationInfo.MapSid;
                return sid == string.Empty || sid != playerLoc.MapSid;
            }

            bool SameMapPredicate(ServerPlayer player)
            {
                string sid = player.LocationInfo.MapSid;
                return sid != string.Empty && sid == playerLoc.MapSid;
            }
        }
        finally
        {
            serverState.StateLock.ExitReadLock();
        }

        await changerTask;
        await normalTask;
        await sameMapTask;
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerMapRoomChanged packet)
    {
        var player = connection.Player;
        var playerLoc = player.LocationInfo;
        logger.LogDebug(
            AppEvents.GameState,
            "{p} map room changed: from room {p} to {a}.",
            player.Info, playerLoc.MapRoom,
            packet.MapRoom
        );
        serverState.StateLock.EnterWriteLock();
        playerLoc.MapRoom = packet.MapRoom;
        serverState.StateLock.ExitWriteLock();
        await BroadcastOthersAsync(new PacketPlayerMapRoomChangedNotification(player.ID, packet), connection);
    }
}