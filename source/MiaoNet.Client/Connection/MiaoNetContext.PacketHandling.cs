using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetContext
{
    public delegate void PlayerNotificationHandler(OnlinePlayer player);
    public delegate void PlayerNotificationHandler<TPacket>(OnlinePlayer player, TPacket packet);

    public event Action<ClientState>? ClientInitialized;
    public event Action<OnlinePlayer>? PlayerJoined;
    public event Action<OnlinePlayer>? PlayerLeft;
    public event PlayerNotificationHandler<PacketPlayerFrame>? PlayerFrameNotification;
    public event PlayerNotificationHandler<PacketPlayerLocationChangedNotification>? PlayerLocationChanged;
    public event Action<PacketPlayerLocationChangedResponse>? PlayerLocationChangeResponded;
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
    public event PlayerNotificationHandler<PacketPlayerChannelMovedNotification>? PlayerChannelMoved;

    private void RegisterPacketHandlers(PacketHandlerRegister r)
    {
        r.Register<PacketPlayerJoined>(HandlePacket);
        r.Register<PacketPlayerLeft>(HandlePacket);
        r.Register<PacketPlayerFrame>(HandlePacket);
        r.Register<PacketPlayerLocationChangedNotification>(HandlePacket);
        r.Register<PacketPlayerLocationChangedResponse>(HandlePacket);
        r.Register<PacketChatMessage>(HandlePacket);
        r.Register<PacketEmote>(HandlePacket);
        r.Register<PacketEmoteText>(HandlePacket);
        r.Register<PacketPlayerLiveState>(HandlePacket);
        r.Register<PacketUpdateGlobalFlag>(HandlePacket);
        r.Register<PacketBeTeleportedRequest>(HandlePacket);
        r.Register<PacketPingData>(HandlePacket);
        r.Register<PacketCreateFireworks>(HandlePacket);
        r.Register<PacketDisconnected>(HandlePacket);
        r.Register<PacketPlayerGrabPlayer>(HandlePacket);
        r.Register<PacketPlayerGrabJumpOut>(HandlePacket);
        r.Register<PacketPlayerPlayedAudio>(HandlePacket);
        r.Register<PacketPlayerChannelMovedResponse>(HandlePacket);
        r.Register<PacketPlayerChannelMovedNotification>(HandlePacket);
        r.Register<PacketChannelCreated>(HandlePacket);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketDisconnected packet)
    {
        OnDisconnected();
        if (packet.Reason == DisconnectReason.Kicked && packet.Message is not null)
        {
            StatusComponent.ShowStatusMessage(ConnectionStatus.Kicked(packet.Message));
            return;
        }
        Logger.Info(LT.MiaoNetConnection, $"Received a disconnect packet: reason {packet.Reason}, message \"{packet.Message}\".");
        StatusComponent.ShowStatusMessage(packet.Message ?? ConnectionStatus.Disconnected);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerJoined packet)
    {
        EnsureState();
        var player = ClientState.OnNewPlayerJoined(packet.ChannelID, packet.PlayerID, packet.PlayerInfo, PlayerGlobalFlags.None);
        PlayerJoined?.Invoke(player);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerLeft packet)
    {
        EnsureState();
        int playerID = packet.PlayerID;
        var player = ClientState.GetPlayer(playerID);
        ClientState.OnPlayerLeft(playerID);
        frameQueues.Remove(playerID);
        PlayerLeft?.Invoke(player);
        player.State = null;
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerFrame packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(envelope.SenderPlayerID, out OnlinePlayer? player))
            return;
        var state = player.State;
        if (state is not null)
        {
            state.ApplyDelta(packet.StateDelta);
        }
        else
        {
            Logger.Warn(LT.MiaoNetSync, $"Received a frame notification for {player.Info}, but there is no initial state.");
            return;
        }
        GetFrameQueue(player.ID).Enqueue(packet.StateDelta);
        PlayerFrameNotification?.Invoke(player, packet);
    }

    private void ConsumeFrameQueues()
    {
        EnsureState();
        if (frameQueues.Count == 0)
            return;

        foreach (var pair in frameQueues)
        {
            var queue = pair.Value;
            if (queue.Count == 0)
                continue;

            if (!ClientState.TryGetPlayer(pair.Key, out OnlinePlayer? player))
                continue;

            int consumeCount = queue.Count > FrameQueueBacklogThreshold ? queue.Count : 1;
            if (consumeCount > 1)
            {
                Logger.Debug(
                    LT.MiaoNetSync,
                    $"Frame queue backlog for {player.Info}: {queue.Count} frames queued, consuming {consumeCount} this frame to catch up."
                );
            }
            for (int i = 0; i < consumeCount && queue.TryDequeue(out PlayerStateDelta? delta); i++)
                FrameTick?.Invoke(player, delta!);
        }
    }

    private Queue<PlayerStateDelta> GetFrameQueue(int playerID)
    {
        if (!frameQueues.TryGetValue(playerID, out Queue<PlayerStateDelta>? queue))
        {
            queue = new();
            frameQueues[playerID] = queue;
        }
        return queue;
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerLocationChangedNotification packet)
    {
        EnsureState();
        int playerID = envelope.SenderPlayerID;
        var player = ClientState.GetPlayer(playerID);
        player.Location = packet.Location;

        frameQueues.Remove(playerID);

        bool roomOnly = packet.InitialState is null
            && packet.Location.IsInMap
            && ClientState.Self.Location.Map == packet.Location.Map;

        if (!roomOnly)
            player.State = packet.InitialState;

        PlayerLocationChanged?.Invoke(player, packet);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerLocationChangedResponse packet)
    {
        EnsureState();
        frameQueues.Clear();
        foreach (var playerInMap in packet.Players)
            ClientState.ApplyPlayerMovedInitialData(playerInMap);
        PlayerLocationChangeResponded?.Invoke(packet);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketChatMessage packet)
    {
        EnsureState();
        OnlinePlayer? player = null;
        if (packet.SourcePlayer is not null)
            player = ClientState.GetPlayerOrSelf((int)packet.SourcePlayer);
        ChatMessageReceived?.Invoke(player, packet);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketEmote packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(envelope.SenderPlayerID);
        EmoteReceived?.Invoke(player, packet.Emote);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketEmoteText packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(envelope.SenderPlayerID);
        EmoteTextReceived?.Invoke(player, packet.Text);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerLiveState packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(envelope.SenderPlayerID);
        if (packet.Type is not LiveStateType.Die)
        {
            var state = player.State;
            if (state is not null)
            {
                state.Position = packet.Vector2;
            }
            else
            {
                Logger.Warn(LT.MiaoNetSync, $"Received a live state notification for {player.Info}, but there is no initial state.");
            }
        }
        PlayerLiveStateNotification?.Invoke(player, packet.Type, packet.Vector2);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketUpdateGlobalFlag packet)
    {
        EnsureState();
        var player = ClientState.GetPlayer(envelope.SenderPlayerID);
        var p = player.GlobalFlags;
        player.GlobalFlags = packet.Flags;
        PlayerGlobalFlagsChanged?.Invoke(player, p);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketBeTeleportedRequest request)
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
        Response(envelope, new PacketBeTeleportedResponse(
            PlayerSessionData.CreateFrom(level!.Session, position)
        ));
        return;

    Reject:
        Response(envelope, new PacketBeTeleportedResponse(null));
        return;
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPingData packet)
    {
        EnsureState();
        foreach (var (playerID, ping) in packet.Data)
            if (ClientState.TryGetPlayerOrSelf(playerID, out var player))
                player.LastPing = ping;
        PingDataReceived?.Invoke();
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerGrabPlayer packet)
    {
        EnsureState();
        PlayerGrabPlayer?.Invoke(ClientState.GetPlayer(packet.PlayerID), packet.IsRelease ? packet.Force : null);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerGrabJumpOut packet)
    {
        EnsureState();
        PlayerGrabJumpOut?.Invoke(ClientState.GetPlayer(packet.PlayerID));
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerPlayedAudio packet)
    {
        EnsureState();
        PlayerAudioPlayed?.Invoke(ClientState.GetPlayer(envelope.SenderPlayerID), packet.PlayerPlayedAudio);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketCreateFireworks packet)
    {
        EnsureState();
        var player = ClientState.Players[envelope.SenderPlayerID];
        PlayerCreatedFireworks?.Invoke(player, packet.Color, packet.InitialSpeed);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerChannelMovedResponse packet)
    {
        EnsureState();
        ClientState.OnSelfChannelMove(packet.ChannelID, packet.ChannelPlayers);
        if (packet.Players is not null)
        {
            foreach (var playerInMap in packet.Players)
                ClientState.ApplyPlayerMovedInitialData(playerInMap);
        }
        frameQueues.Clear();
        SelfChannelMoved?.Invoke(packet);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketPlayerChannelMovedNotification packet)
    {
        EnsureState();
        int playerID = envelope.SenderPlayerID;
        ClientState.OnPlayerChannelMove(playerID, packet.ChannelID, packet.Presence, out var pl);
        if (packet.InitialData is not null)
            ClientState.ApplyPlayerMovedInitialData(playerID, packet.InitialData.Value);
        frameQueues.Remove(playerID);
        PlayerChannelMoved?.Invoke(pl, packet);
    }

    private void HandlePacket(PacketEnvelope envelope, PacketChannelCreated packet)
    {
        EnsureState();
        ClientState.OnNewChannelCreated(packet.ChannelID, packet.ChannelInfo);
    }
}
