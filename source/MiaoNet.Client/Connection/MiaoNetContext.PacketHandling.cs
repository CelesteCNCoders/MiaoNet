using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public partial class MiaoNetContext
{
    private void HandlePacket(PacketClientInitial packet)
    {
        clientState = new(packet, PlayerLocation.Empty);
        ClientInitialized?.Invoke(clientState);
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
        var state = player.State;
        if (state is not null)
        {
            var p = packet.Packet;
            state.X = p.X;
            state.Y = p.Y;
            if (p.DashesChange)
                state.Dashes = p.Dashes;
            if (p.Flags.HasFlag(PacketPlayerFrame.FrameFlags.StartDash))
                state.Dashing = true;
            if (p.Flags.HasFlag(PacketPlayerFrame.FrameFlags.EndDash))
                state.Dashing = false;
        }
        else
        {
            Logger.Error(nameof(MiaoNetContext), $"No initial state but received frame notification for {player.Info}!");
            Disconnect();
            return;
        }
        PlayerFrameNotification?.Invoke(player, packet.Packet);
    }

    private void HandlePacket(PacketPlayerMapChangedNotification packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        player.Location = packet.Location;
        player.State = packet.InitialState;
        player.GraphicsInfo = packet.GraphicsInfo;
        PlayerMapChanged?.Invoke(player, packet);
    }

    private void HandlePacket(PacketPlayerMapRoomChangedNotification packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        player.Location.MapRoom = packet.Packet.MapRoom;
        PlayerMapRoomChanged?.Invoke(player, packet.Packet.MapRoom);
    }

    private void HandlePacket(PacketPlayerMapChangedResponse packet)
    {
        EnsureState();
        PlayerMapChangeResponse?.Invoke(packet);
    }
}
