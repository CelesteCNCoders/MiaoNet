using Microsoft.Extensions.Logging;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    private void RegisterPacketHandlers(PacketHandlerRegister register)
    {
        register.Register<PacketPlayerFrame>(HandlePacket);
        register.Register<PacketPlayerMapChanged>(HandlePacket);
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerFrame packet)
    {
        // TODO set state
        await BroadcastOthersAsync(new PacketPlayerFrameNotify(connection.ID, packet), connection);
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        var playerLoc = player.LocationInfo;
        logger.LogDebug("{p} map changed: {s}:{r}.", player.Info, packet.MapSid, packet.MapRoom);

        if (!string.IsNullOrEmpty(packet.MapSid))
        {
            playerLoc.MapSid = packet.MapSid;
            playerLoc.MapRoom = packet.MapRoom;
        }
        else
        {
            if (string.IsNullOrEmpty(packet.MapRoom))
                playerLoc.MapRoom = packet.MapRoom;
            else
                playerLoc.MapSid = playerLoc.MapRoom = string.Empty;
        }

        IPacket normal = new PacketPlayerMapChangedNotify(
            player.Info.ID, 
            playerLoc.MapSid, playerLoc.MapRoom
        );
        IPacket sameMap = new PacketPlayerMapChangedNotify(
            player.Info.ID, 
            string.Empty, playerLoc.MapRoom, 
            null, packet.InitialState
        );

        Task normalTask = BroadcastToOthersAsync(normal,
            c => c.Player.LocationInfo.MapSid != playerLoc.MapSid, player.ID
        );
        Task sameMapTask = BroadcastToOthersAsync(sameMap,
            c => c.Player.LocationInfo.MapSid == playerLoc.MapSid, player.ID
        );

        await sameMapTask;
        await normalTask;
    }
}