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
        await BroadcastOthersAsync(new PacketPlayerFrameNotify(connection.ID, packet), connection);
    }

    private async ValueTask HandlePacket(MiaoClientConnection connection, PacketPlayerMapChanged packet)
    {
        var player = connection.Player;
        logger.LogDebug("{p} map changed: {s}:{r}.", player, packet.MapSid, packet.MapRoom);

        var info = player.StateInfo;

        IPacket normal = new PacketPlayerMapChangedNotify(player.Info.ID, packet);
        IPacket sameMap = new PacketPlayerMapChangedNotify(player.Info.ID, packet, null, packet.InitialStats);

        Task normalTask = BroadcastToAsync(normal, c => c.Player.ID != player.ID && c.Player.StateInfo.MapSid != info.MapSid);
        Task sameMapTask = BroadcastToAsync(sameMap, c => c.Player.ID != player.ID && c.Player.StateInfo.MapSid == info.MapSid);

        await sameMapTask;
        await normalTask;
    }
}