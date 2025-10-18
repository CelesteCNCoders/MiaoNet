using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public partial class MiaoNetContext
{
    private void HandlePacket(PacketClientInitial packet)
    {
        PlayerLocationInfo locationInfo;
        if (Engine.Scene is Level level)
            locationInfo = new(level.Session.Area.SID, level.Session.Level);
        else
            locationInfo = new(string.Empty, string.Empty);

        ClientState = new(packet, locationInfo);
        ClientInitialized?.Invoke(ClientState);
    }

    private void HandlePacket(PacketPlayerJoined packet)
    {
        EnsureState();
        var player = ClientState.OnNewPlayerJoined(packet);
        PlayerJoined?.Invoke(player);
    }

    private void HandlePacket(PacketPlayerLeft packet)
    {
        EnsureState();
        PlayerLeft?.Invoke(ClientState.Players[packet.PlayerID]);
        ClientState.OnPlayerLeft(packet.PlayerID);
    }

    private void HandlePacket(PacketPlayerFrameNotification packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        PlayerFrameNotification?.Invoke(player, packet.Packet);
    }

    private void HandlePacket(PacketPlayerMapChangedNotification packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        player.LocationInfo.MapSid = packet.MapSid;
        player.LocationInfo.MapRoom = packet.MapRoom;
        PlayerMapChanged?.Invoke(player, packet);
    }

    private void HandlePacket(PacketPlayerMapRoomChangedNotification packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        player.LocationInfo.MapRoom = packet.Packet.MapRoom;
        PlayerMapRoomChanged?.Invoke(player, packet.Packet.MapRoom);
    }

    private void HandlePacket(PacketPlayerMapChangedResponse packet)
    {
        EnsureState();
        PlayerMapChangeResponse?.Invoke(packet);
    }
}
