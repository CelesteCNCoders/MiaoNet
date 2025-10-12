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
            connection.Player.State = new(packet.X, packet.Y, 2); // HERE
        }
        else
        {
            var state = connection.Player.State;
            state.X = packet.X;
            state.Y = packet.Y;
            state.Dashes = 1;
        }
        await BroadcastToOthersAsync(
            new PacketPlayerFrameNotify(connection.ID, packet),
            con => con.Player.LocationInfo.MapSid == connection.Player.LocationInfo.MapSid,
            connection
        );
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        var playerLoc = player.LocationInfo;
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
        try
        {
            IPacket normal = new PacketPlayerMapChangedNotify(
                player.Info.ID,
                playerLoc.MapSid, playerLoc.MapRoom
            );
            IPacket sameMap = new PacketPlayerMapChangedNotify(
                player.Info.ID,
                playerLoc.MapSid, playerLoc.MapRoom,
                null, packet.InitialState
            );
            // TODO toSameMap & inSameMap

            normalTask = BroadcastToOthersAsync(normal, NormalPredicate, player.ID);
            sameMapTask = BroadcastToOthersAsync(sameMap, SameMapPredicate, player.ID);

            bool NormalPredicate(MiaoClientConnection con)
            {
                string sid = con.Player.LocationInfo.MapSid;
                return sid == string.Empty || sid != playerLoc.MapSid;
            }

            bool SameMapPredicate(MiaoClientConnection con)
            {
                string sid = con.Player.LocationInfo.MapSid;
                return sid != string.Empty && sid == playerLoc.MapSid;
            }
        }
        finally
        {
            serverState.StateLock.ExitReadLock();
        }

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
        await BroadcastOthersAsync(new PacketPlayerMapRoomChangedNotify(player.ID, packet), connection);
    }
}