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
    public event Action<OnlinePlayer, PlayerOnlineStatus>? PlayerOnlineStatusChanged;

    private void RegisterPacketHandlers(PacketHandlerRegister r)
    {
        r.Register<PacketPlayerJoined>(HandlePacket);
        r.Register<PacketPlayerLeft>(HandlePacket);
        r.Register<PacketContextualPlayerNotification<PacketPlayerFrame>>(HandlePacket);
        r.Register<PacketPlayerMapChangedNotification>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketPlayerMapRoomChanged>>(HandlePacket);
        r.Register<PacketPlayerMapChangedResponse>(HandlePacket);
        r.Register<PacketChatMessage>(HandlePacket);
        r.Register<PacketEmote>(HandlePacket);
        r.Register<PacketEmoteText>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketPlayerStateFlags>>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketUpdateOnlineStatus>>(HandlePacket);
        r.Register<PacketBeTeleportedRequest>(HandlePacket);
    }

    private void HandlePacket(PacketPlayerJoined packet)
    {
        EnsureState();
        var player = ClientState.OnNewPlayerJoined(packet.ChannelID, packet.PlayerInfo, packet.OnlineStatus);
        PlayerJoined?.Invoke(player);
        ChatComponent.OnNotifyMessage(Dialog.Clean("miaonet_context_player_joined").Replace("(0)", player.Info.Name));
    }

    private void HandlePacket(PacketPlayerLeft packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        ClientState.OnPlayerLeft(packet.PlayerID);
        PlayerLeft?.Invoke(player);
        ChatComponent.OnNotifyMessage(Dialog.Clean("miaonet_context_player_left").Replace("(0)", player.Info.Name));
    }

    private void HandlePacket(PacketContextualPlayerNotification<PacketPlayerFrame> packet)
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
            state.Dashing = packet.Packet.Dashing;
        }
        else
        {
            Logger.Error(nameof(MiaoNetContext), $"No initial state but received frame notification for {player.Info}!");
            // TODO this is a potential bug
            //Disconnect();
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

    private void HandlePacket(PacketPlayerNotification<PacketUpdateOnlineStatus> packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        var p = player.OnlineStatus;
        player.OnlineStatus = packet.Packet.Status;
        PlayerOnlineStatusChanged?.Invoke(player, p);
    }

    private void HandlePacket(PacketBeTeleportedRequest request)
    {
        EnsureState();
        Level? level = Engine.Scene as Level;
        Player? player = level?.Tracker.GetEntity<Player>();
        if (player is null)
        {
            Response(request, new PacketBeTeleportedResponse(null));
            return;
        }
        else
        {
            Response(request, new PacketBeTeleportedResponse(
                PlayerSessionData.CreateFrom(level!.Session, player.Position)
            ));
        }
    }
}
