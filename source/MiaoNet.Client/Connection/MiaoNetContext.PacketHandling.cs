using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public partial class MiaoNetContext
{
    public delegate void PacketPlayerNotificationHandler(OnlinePlayer player);
    public delegate void PacketPlayerNotificationHandler<TPacket>(OnlinePlayer player, TPacket packet);

    public event Action<ClientState>? ClientInitialized;
    public event Action<OnlinePlayer>? PlayerJoined;
    public event Action<OnlinePlayer>? PlayerLeft;
    public event PacketPlayerNotificationHandler<PacketPlayerFrame>? PlayerFrameNotification;
    public event PacketPlayerNotificationHandler<PacketPlayerMapChangedNotification>? PlayerMapChanged;
    public event Action<OnlinePlayer, string>? PlayerMapRoomChanged;
    public event Action<PacketPlayerMapChangedResponse>? PlayerMapChangeResponded;
    public event Action<OnlinePlayer?, PacketChatMessage>? ChatMessageReceived;
    public event Action<OnlinePlayer, EmoteData>? EmoteReceived;
    public event Action<OnlinePlayer, string>? EmoteTextReceived;
    public event Action<OnlinePlayer, PacketPlayerStateFlags.StateFlags>? PlayerStateFlagsNotification;

    private void RegisterPacketHandlers(PacketHandlerRegister r)
    {
        r.Register<PacketClientInitial>(HandlePacket);
        r.Register<PacketPlayerJoined>(HandlePacket);
        r.Register<PacketPlayerLeft>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketPlayerFrame>>(HandlePacket);
        r.Register<PacketPlayerMapChangedNotification>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketPlayerMapRoomChanged>>(HandlePacket);
        r.Register<PacketPlayerMapChangedResponse>(HandlePacket);
        r.Register<PacketChatMessage>(HandlePacket);
        r.Register<PacketEmote>(HandlePacket);
        r.Register<PacketEmoteText>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketPlayerStateFlags>>(HandlePacket);
    }

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
        var player = ClientState.Players[packet.PlayerID];
        ClientState.OnPlayerLeft(packet.PlayerID);
        PlayerLeft?.Invoke(player);
    }

    private void HandlePacket(PacketPlayerNotification<PacketPlayerFrame> packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        var state = player.State;
        if (state is not null)
        {
            var p = packet.Packet;
            state.Position = p.Position;
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

    private void HandlePacket(PacketPlayerNotification<PacketPlayerMapRoomChanged> packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        player.Location.MapRoom = packet.Packet.MapRoom;
        PlayerMapRoomChanged?.Invoke(player, packet.Packet.MapRoom);
    }

    private void HandlePacket(PacketPlayerMapChangedResponse packet)
    {
        EnsureState();
        foreach (var playerInMap in packet.PlayersInMap)
        {
            var player = ClientState.Players[playerInMap.PlayerID];
            player.State = playerInMap.State;
            player.GraphicsInfo = playerInMap.GraphicsInfo;
        }
        PlayerMapChangeResponded?.Invoke(packet);
    }

    private void HandlePacket(PacketChatMessage packet)
    {
        EnsureState();
        OnlinePlayer? player = null;
        if (packet.SourcePlayer.HasValue)
        {
            int id = (int)packet.SourcePlayer;
            player = id == ClientState.Self.ID ? ClientState.Self : ClientState.Players[id];
        }
        ChatMessageReceived?.Invoke(player, packet);
    }

    private void HandlePacket(PacketEmote packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        EmoteReceived?.Invoke(player, packet.Emote);
    }

    private void HandlePacket(PacketEmoteText packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        EmoteTextReceived?.Invoke(player, packet.Text);
    }

    private void HandlePacket(PacketPlayerNotification<PacketPlayerStateFlags> packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        PlayerStateFlagsNotification?.Invoke(player, packet.Packet.Flags);
    }
}
