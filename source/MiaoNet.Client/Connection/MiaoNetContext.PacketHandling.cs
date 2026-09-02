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
    public event PacketPlayerNotificationHandler<PacketPlayerLocationChangedNotification>? PlayerLocationChanged;
    public event Action<PacketPlayerLocationChangedResponse>? PlayerLocationChangeResponded;
    public event Action<OnlinePlayer?, PacketChatMessage>? ChatMessageReceived;
    public event Action<OnlinePlayer, EmoteData>? EmoteReceived;
    public event Action<OnlinePlayer, string>? EmoteTextReceived;
    public event PacketPlayerNotificationHandler<PacketPlayerLiveState>? PlayerLiveStateNotification;
    public event Action<OnlinePlayer, PlayerGlobalFlags>? PlayerGlobalFlagsChanged;
    public event Action<OnlinePlayer, Color, float>? PlayerCreatedFireworks;
    public event Action? PingDataReceived;
    public event Action<OnlinePlayer, PlayerPlayedAudio>? PlayerAudioPlayed;
    public event Action<OnlinePlayer, Vector2?>? PlayerGrabPlayer;
    public event Action<OnlinePlayer>? PlayerGrabJumpOut;
    public event Action<PacketPlayerChannelMovedResponse>? SelfChannelMoved;
    public event PacketPlayerNotificationHandler<PacketPlayerChannelMovedNotification>? PlayerChannelMoved;
    public event Action<PacketWatchSnapshotRequest>? WatchSnapshotRequested;
    public event Action<PacketWatchSceneDeltaNotification>? WatchSceneDeltaReceived;
    public event Action<PacketWatchResyncSnapshot>? WatchResyncSnapshotReceived;
    public event Action<PacketWatchTargetRestartingNotification>? WatchTargetRestarting;
    public event Action<PacketWatchProducerStop>? WatchProducerStopped;
    public event Action<PacketWatchEnded>? WatchEnded;

    private void RegisterPacketHandlers(PacketHandlerRegister r)
    {
        r.Register<PacketPlayerJoined>(HandlePacket);
        r.Register<PacketPlayerLeft>(HandlePacket);
        r.Register<PacketContextualPlayerNotification<PacketPlayerFrame>>(HandlePacket);
        r.Register<PacketPlayerLocationChangedNotification>(HandlePacket);
        r.Register<PacketPlayerLocationChangedResponse>(HandlePacket);
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
        r.Register<PacketWatchSnapshotRequest>(HandlePacket);
        r.Register<PacketWatchSceneDeltaNotification>(HandlePacket);
        r.Register<PacketWatchResyncSnapshot>(HandlePacket);
        r.Register<PacketWatchTargetRestartingNotification>(HandlePacket);
        r.Register<PacketWatchProducerStop>(HandlePacket);
        r.Register<PacketWatchEnded>(HandlePacket);
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
    }

    private void HandlePacket(PacketContextualPlayerNotification<PacketPlayerFrame> packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;

        PacketPlayerFrame frame = packet.Packet;
        if (frame.PlayerEpoch < player.PlayerEpoch)
            return;
        if (frame.PlayerEpoch > player.PlayerEpoch)
        {
            Logger.Warn(
                LT.MiaoNetSync,
                $"Ignored future PlayerFrame epoch {frame.PlayerEpoch} for {player.Info}; current epoch is {player.PlayerEpoch}."
            );
            return;
        }
        if (frame.PlayerSequence <= player.LastPlayerSequence)
            return;

        if (frame.Kind == PlayerFrameKind.Keyframe)
        {
            player.State = frame.KeyframeState!.Clone();
            player.LastPlayerSequence = frame.PlayerSequence;
            player.AwaitingPlayerKeyframe = false;
        }
        else
        {
            if (player.AwaitingPlayerKeyframe
                || frame.PlayerSequence != PlayerTimelineSequence.Next(player.LastPlayerSequence))
            {
                player.AwaitingPlayerKeyframe = true;
                Logger.Warn(
                    LT.MiaoNetSync,
                    $"PlayerFrame gap for {player.Info}: expected {PlayerTimelineSequence.Next(player.LastPlayerSequence)}, received {frame.PlayerSequence}; waiting for Keyframe."
                );
                return;
            }

            PlayerState? state = player.State;
            if (state is null)
            {
                Logger.Warn(LT.MiaoNetSync, $"No initial state but received frame notification for {player.Info}!");
                player.AwaitingPlayerKeyframe = true;
                return;
            }
            state.ApplyDelta(frame.StateDelta!);
            player.LastPlayerSequence = frame.PlayerSequence;
        }
        PlayerFrameNotification?.Invoke(player, frame);
    }

    private void HandlePacket(PacketPlayerLocationChangedNotification packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
        if (packet.PlayerEpoch < player.PlayerEpoch)
            return;
        player.Location = packet.Location;
        player.PlayerEpoch = packet.PlayerEpoch;
        player.LastPlayerSequence = packet.PlayerSequence;
        player.AwaitingPlayerKeyframe = false;
        player.State = packet.InitialState;
        player.EpochBaselineState = packet.InitialState?.Clone();

        PlayerLocationChanged?.Invoke(player, packet);
    }

    private void HandlePacket(PacketPlayerLocationChangedResponse packet)
    {
        EnsureState();
        foreach (var playerInMap in packet.Players)
        {
            if (ClientState.TryGetPlayer(playerInMap.PlayerID, out OnlinePlayer? player))
                ClientState.ApplyPlayerMovedInitialData(player, playerInMap.InitialData);
        }
        PlayerLocationChangeResponded?.Invoke(packet);
    }

    private void HandlePacket(PacketChatMessage packet)
    {
        EnsureState();
        OnlinePlayer? player = null;
        if (packet.SourcePlayer is int sourcePlayer
            && !ClientState.TryGetPlayerOrSelf(sourcePlayer, out player))
            return;
        ChatMessageReceived?.Invoke(player, packet);
    }

    private void HandlePacket(PacketEmote packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
        EmoteReceived?.Invoke(player, packet.Emote);
    }

    private void HandlePacket(PacketEmoteText packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
        EmoteTextReceived?.Invoke(player, packet.Text);
    }

    private void HandlePacket(PacketPlayerNotification<PacketPlayerLiveState> packet)
    {
        EnsureState();
        var p = packet.Packet;
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
        if (p.PlayerEpoch != player.PlayerEpoch
            || p.PlayerSequence != PlayerTimelineSequence.Next(player.LastPlayerSequence)
            || player.AwaitingPlayerKeyframe)
        {
            if (p.PlayerEpoch == player.PlayerEpoch && p.PlayerSequence > player.LastPlayerSequence)
                player.AwaitingPlayerKeyframe = true;
            return;
        }
        player.LastPlayerSequence = p.PlayerSequence;
        if (p.Type is LiveStateType.Respawn or LiveStateType.RespawnFromSL)
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
        PlayerLiveStateNotification?.Invoke(player, packet.Packet);
    }

    private void HandlePacket(PacketPlayerNotification<PacketUpdateGlobalFlag> packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
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
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            return;
        PlayerGrabPlayer?.Invoke(player, packet.IsRelease ? packet.Force : null);
    }

    private void HandlePacket(PacketPlayerGrabJumpOut packet)
    {
        EnsureState();
        if (ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            PlayerGrabJumpOut?.Invoke(player);
    }

    private void HandlePacket(PacketContextualPlayerNotification<PacketPlayerPlayedAudio> packet)
    {
        EnsureState();
        if (ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            PlayerAudioPlayed?.Invoke(player, packet.Packet.PlayerPlayedAudio);
    }

    private void HandlePacket(PacketPlayerNotification<PacketCreateFireworks> packet)
    {
        EnsureState();
        if (ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? player))
            PlayerCreatedFireworks?.Invoke(player, packet.Packet.Color, packet.Packet.InitialSpeed);
    }

    private void HandlePacket(PacketPlayerChannelMovedResponse packet)
    {
        EnsureState();
        ClientState.OnSelfChannelMove(packet.ChannelID, packet.ChannelPlayers);
        ClientState.Self.PlayerEpoch = packet.PlayerEpoch;
        ClientState.Self.LastPlayerSequence = packet.PlayerSequence;
        ClientState.Self.AwaitingPlayerKeyframe = false;
        if (packet.Players is not null)
        {
            foreach (var playerInMap in packet.Players)
            {
                if (ClientState.TryGetPlayer(playerInMap.PlayerID, out OnlinePlayer? player))
                    ClientState.ApplyPlayerMovedInitialData(player, playerInMap.InitialData);
            }
        }
        SelfChannelMoved?.Invoke(packet);
    }

    private void HandlePacket(PacketPlayerChannelMovedNotification packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.PlayerID, out OnlinePlayer? existing))
            return;
        if (packet.PlayerEpoch < existing.PlayerEpoch)
            return;
        ClientState.OnPlayerChannelMove(packet.PlayerID, packet.ChannelID, packet.Presence, out var pl);
        if (packet.PlayerEpoch < pl.PlayerEpoch)
            return;
        pl.PlayerEpoch = packet.PlayerEpoch;
        pl.LastPlayerSequence = packet.PlayerSequence;
        pl.AwaitingPlayerKeyframe = false;
        if (packet.InitialData is not null)
            ClientState.ApplyPlayerMovedInitialData(packet.PlayerID, packet.InitialData.Value);
        PlayerChannelMoved?.Invoke(pl, packet);
    }

    private void HandlePacket(PacketChannelCreated packet)
    {
        EnsureState();
        ClientState.OnNewChannelCreated(packet.ChannelID, packet.ChannelInfo);
    }

    private void HandlePacket(PacketWatchSnapshotRequest packet)
    {
        EnsureState();
        WatchSnapshotRequested?.Invoke(packet);
    }

    private void HandlePacket(PacketWatchSceneDeltaNotification packet)
    {
        EnsureState();
        WatchSceneDeltaReceived?.Invoke(packet);
    }

    private void HandlePacket(PacketWatchResyncSnapshot packet)
    {
        EnsureState();
        WatchResyncSnapshotReceived?.Invoke(packet);
    }

    private void HandlePacket(PacketWatchTargetRestartingNotification packet)
    {
        EnsureState();
        if (!ClientState.TryGetPlayer(packet.TargetPlayerID, out OnlinePlayer? player)
            || packet.PlayerEpoch != player.PlayerEpoch
            || packet.PlayerSequence <= player.LastPlayerSequence)
            return;

        if (packet.PlayerSequence != PlayerTimelineSequence.Next(player.LastPlayerSequence))
            player.AwaitingPlayerKeyframe = true;
        player.LastPlayerSequence = packet.PlayerSequence;
        WatchTargetRestarting?.Invoke(packet);
    }

    private void HandlePacket(PacketWatchProducerStop packet)
    {
        EnsureState();
        WatchProducerStopped?.Invoke(packet);
    }

    private void HandlePacket(PacketWatchEnded packet)
    {
        EnsureState();
        WatchEnded?.Invoke(packet);
    }
}
