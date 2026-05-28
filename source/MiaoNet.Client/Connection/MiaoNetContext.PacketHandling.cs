using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetContext
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
    public event Action<OnlinePlayer, LiveStateType, Vector2>? PlayerLiveStateNotification;
    public event Action<OnlinePlayer, PlayerGlobalFlags>? PlayerGlobalFlagsChanged;
    public event Action<OnlinePlayer, Color, float>? PlayerCreatedFireworks;
    public event Action? PingDataReceived;
    public event Action<OnlinePlayer, PlayerPlayedAudio>? PlayerAudioPlayed;
    public event Action<OnlinePlayer, Vector2?>? PlayerGrabPlayer;
    public event Action<OnlinePlayer>? PlayerGrabJumpOut;
    public event Action<PacketPlayerChannelMovedResponse>? SelfChannelMoved;
    public event PacketPlayerNotificationHandler<PacketPlayerChannelMovedNotification>? PlayerChannelMoved;
    public event Action<OnlineChannel>? ChannelCreated;
    public event Action<OnlineChannel>? ChannelRemoved;

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
        r.Register<PacketPlayerNotification<PacketPlayerLiveState>>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketUpdateGlobalFlag>>(HandlePacket);
        r.Register<PacketBeTeleportedRequest>(HandlePacket);
        r.Register<PacketPingData>(HandlePacket);
        r.Register<PacketPlayerNotification<PacketCreateFireworks>>(HandlePacket);
        r.Register<PacketDisconnected>(HandlePacket);
        r.Register<PacketPlayerGrabPlayer>(HandlePacket);
        r.Register<PacketPlayerGrabJumpOut>(HandlePacket);
        r.Register<PacketContextualPlayerNotification<PacketPlayerPlayedAudio>>(HandlePacket);
        r.Register<PacketPlayerChannelMovedResponse>(HandlePacket);
        r.Register<PacketPlayerChannelMovedNotification>(HandlePacket);
        r.Register<PacketChannelCreated>(HandlePacket);
    }

    private void HandlePacket(PacketDisconnected packet)
    {
        OnDisconnected();
        if (packet.Reason == DisconnectReason.Kicked && packet.Message is not null)
        {
            StatusComponent.ShowStatusMessage(ConnectionStatus.Kicked(packet.Message));
            return;
        }
        Logger.Info(LT.MiaoNetConnection, $"Received PacketDisconnected with reason {packet.Reason} and message \"{packet.Message}\".");
        StatusComponent.ShowStatusMessage(packet.Message ?? ConnectionStatus.Disconnected);
    }

    private void HandlePacket(PacketPlayerJoined packet)
    {
        EnsureState();
        var player = ClientState.OnNewPlayerJoined(packet.ChannelID, packet.PlayerID, packet.PlayerInfo, PlayerGlobalFlags.None);
        PlayerJoined?.Invoke(player);
    }

    private void HandlePacket(PacketPlayerLeft packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(packet.PlayerID);
        ClientState.OnPlayerLeft(packet.PlayerID);
        PlayerLeft?.Invoke(player);
        player.State = null;
        HandleChannelRemoveIfEmpty(player.Channel);
    }

    private void HandlePacket(PacketContextualPlayerNotification<PacketPlayerFrame> packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
        var state = player.State;
        if (state is not null)
        {
            PacketPlayerFrame p = packet.Packet;
            state.Position = p.Position;
            state.Dashing = p.Dashing;
            state.FacingLeft = p.FacingLeft;
            state.Interactions = p.Interactions;

            if (p.HasFollowerInitials)
                state.ApplyFollowersInitials(p.FollowerInitials);
            else if (p.HasFollowerDeltas)
                state.ApplyFollowersDeltas(p.FollowerDeltas);

            if (p.DashesChange)
                state.Dashes = p.Dashes;
            if (p.HasWindDirection)
                state.WindDirection = p.WindDirection;
        }
        else
        {
            Logger.Warn(LT.MiaoNetSync, $"No initial state but received frame notification for {player.Info}!");
            return;
        }
        PlayerFrameNotification?.Invoke(player, packet.Packet);
    }

    private void HandlePacket(PacketPlayerMapChangedNotification packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(packet.PlayerID);
        player.Location = packet.Location;
        player.State = packet.InitialState;
        player.GraphicsInfo = packet.GraphicsInfo;
        PlayerMapChanged?.Invoke(player, packet);
    }

    private void HandlePacket(PacketPlayerNotification<PacketPlayerMapRoomChanged> packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(packet.PlayerID);
        player.Location = new PlayerLocation(player.Location.Map, packet.Packet.MapRoom);
        PlayerMapRoomChanged?.Invoke(player, packet.Packet.MapRoom);
    }

    private void HandlePacket(PacketPlayerMapChangedResponse packet)
    {
        EnsureState();
        foreach (var playerInMap in packet.Players)
        {
            var player = ClientState.GetPlayer(playerInMap.PlayerID);
            player.State = playerInMap.InitialState;
            player.GraphicsInfo = playerInMap.GraphicsInfo;
        }
        PlayerMapChangeResponded?.Invoke(packet);
    }

    private void HandlePacket(PacketChatMessage packet)
    {
        EnsureState();
        OnlinePlayer? player = null;
        if (packet.SourcePlayer is not null)
            player = ClientState.GetPlayerOrSelf((int)packet.SourcePlayer);
        ChatMessageReceived?.Invoke(player, packet);
    }

    private void HandlePacket(PacketEmote packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(packet.PlayerID);
        EmoteReceived?.Invoke(player, packet.Emote);
    }

    private void HandlePacket(PacketEmoteText packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(packet.PlayerID);
        EmoteTextReceived?.Invoke(player, packet.Text);
    }

    private void HandlePacket(PacketPlayerNotification<PacketPlayerLiveState> packet)
    {
        EnsureState();
        var p = packet.Packet;
        var player = ClientState.GetPlayer(packet.PlayerID);
        if (p.Type is not LiveStateType.Die)
        {
            var state = player.State;
            if (state is not null)
            {
                state.Position = p.Vector2;
            }
            else
            {
                Logger.Warn(LT.MiaoNetSync, $"No initial state but received live state notification for {player.Info}!");
            }
        }
        PlayerLiveStateNotification?.Invoke(player, packet.Packet.Type, packet.Packet.Vector2);
    }

    private void HandlePacket(PacketPlayerNotification<PacketUpdateGlobalFlag> packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(packet.PlayerID);
        var p = player.GlobalFlags;
        player.GlobalFlags = packet.Packet.Flags;
        PlayerGlobalFlagsChanged?.Invoke(player, p);
    }

    private void HandlePacket(PacketBeTeleportedRequest request)
    {
        EnsureState();
        if (Engine.Scene is not Level level)
            goto Reject;
        Player? player = level.Tracker.GetEntity<Player>();
        Vector2 position;
        if (player is not null)
        {
            position = player.Position;
        }
        else
        {
            PlayerDeadBody? body = level.Entities.FindFirst<PlayerDeadBody>();
            if (body is not null)
                position = body.Position;
            else
                goto Reject;
        }
        Response(request, new PacketBeTeleportedResponse(
            PlayerSessionData.CreateFrom(level!.Session, position)
        ));
        return;

    Reject:
        Response(request, new PacketBeTeleportedResponse(null));
        return;
    }

    private void HandlePacket(PacketPingData packet)
    {
        EnsureState();
        foreach (var (playerID, ping) in packet.Data)
            if (ClientState.TryGetPlayerOrSelf(playerID, out var player))
                player.LastPing = ping;
        PingDataReceived?.Invoke();
    }

    private void HandlePacket(PacketPlayerGrabPlayer packet)
    {
        EnsureState();
        PlayerGrabPlayer?.Invoke(ClientState.GetPlayer(packet.PlayerID), packet.IsRelease ? packet.Force : null);
    }

    private void HandlePacket(PacketPlayerGrabJumpOut packet)
    {
        EnsureState();
        PlayerGrabJumpOut?.Invoke(ClientState.GetPlayer(packet.PlayerID));
    }

    private void HandlePacket(PacketContextualPlayerNotification<PacketPlayerPlayedAudio> packet)
    {
        EnsureState();
        PlayerAudioPlayed?.Invoke(ClientState.GetPlayer(packet.PlayerID), packet.Packet.PlayerPlayedAudio);
    }

    private void HandlePacket(PacketPlayerNotification<PacketCreateFireworks> packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        PlayerCreatedFireworks?.Invoke(player, packet.Packet.Color, packet.Packet.InitialSpeed);
    }

    private void HandlePacket(PacketPlayerChannelMovedResponse packet)
    {
        EnsureState();
        ClientState.OnSelfChannelMove(packet.ChannelID, out var p, out var c);
        if (packet.Players is not null)
        {
            foreach (var playerInMap in packet.Players)
            {
                var player = ClientState.GetPlayer(playerInMap.PlayerID);
                player.State = playerInMap.InitialState;
                player.GraphicsInfo = playerInMap.GraphicsInfo;
            }
        }
        SelfChannelMoved?.Invoke(packet);
        HandleChannelRemoveIfEmpty(p);
    }

    private void HandleChannelRemoveIfEmpty(OnlineChannel channel)
    {
        EnsureState();
        if (channel.Players.Count == 0 && ClientState.SelfChannel != channel && channel.ID != 0)
        {
            ClientState.OnChannelRemoved(channel.ID);
            ChannelRemoved?.Invoke(channel);
        }
    }

    private void HandlePacket(PacketPlayerChannelMovedNotification packet)
    {
        EnsureState();
        ClientState.OnPlayerChannelMove(packet.PlayerID, packet.ChannelID, out var pl, out var p, out var c);
        pl.State = packet.InitialState;
        pl.GraphicsInfo = packet.GraphicsInfo;
        PlayerChannelMoved?.Invoke(pl, packet);
        HandleChannelRemoveIfEmpty(p);
    }

    private void HandlePacket(PacketChannelCreated packet)
    {
        EnsureState();
        var channel = ClientState.OnNewChannelCreated(packet.ChannelID, packet.ChannelInfo);
        ChannelCreated?.Invoke(channel);
    }
}
