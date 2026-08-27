using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ClientState
{
    private readonly Dictionary<int, OnlinePlayer> players;
    private readonly Dictionary<int, OnlineChannel> channels;

    public IReadOnlyDictionary<int, OnlinePlayer> Players => players;

    /// <summary>All players, included self.</summary>
    public IEnumerable<OnlinePlayer> AllPlayers
    {
        get
        {
            yield return Self;
            foreach (OnlinePlayer player in players.Values)
                yield return player;
        }
    }

    public IReadOnlyDictionary<int, OnlineChannel> Channels => channels;

    public OnlinePlayer Self { get; private set; }

    public OnlineChannel SelfChannel => Self.Channel;

    public PlayerState? SelfState { get => Self.State; set => Self.State = value; }

    public event Action? SelfLocationChanged;

    public ClientState(PacketClientInitial clientInitial)
    {
        players = new();
        channels = new();

        channels.Add(
            ChannelInfo.PrivateChannelVirtualID,
            new OnlineChannel(ChannelInfo.PrivateChannelVirtualID, new ChannelInfo(Dialog.Get("miaonet_private_channel_name")))
        );

        foreach (var channel in clientInitial.Channels)
            channels.Add(channel.ID, new OnlineChannel(channel.ID, channel.ChannelInfo));
        foreach (var player in clientInitial.Players)
        {
            var p = AddNewPlayer(player.ChannelID, player.PlayerID, player.PlayerInfo, player.GlobalFlags);
            p.Location = player.Location;
            p.PlayerEpoch = player.PlayerEpoch;
            p.LastPlayerSequence = player.PlayerSequence;
        }
        Self = new(channels[clientInitial.ChannelID], clientInitial.PlayerID, clientInitial.SelfPlayerInfo, PlayerGlobalFlags.None);
    }

    public OnlinePlayer OnNewPlayerJoined(int channelID, int playerID, PlayerInfo playerInfo, PlayerGlobalFlags globalFlags)
        => AddNewPlayer(channelID, playerID, playerInfo, globalFlags);

    private OnlinePlayer AddNewPlayer(int channelID, int playerID, PlayerInfo playerInfo, PlayerGlobalFlags globalFlags)
    {
        var channel = channels[channelID];
        var player = new OnlinePlayer(channel, playerID, playerInfo, globalFlags);
        players.Add(player.ID, player);
        channel.Players.Add(player);
        return player;
    }

    public OnlineChannel OnNewChannelCreated(int channelID, ChannelInfo channelInfo)
    {
        var channel = new OnlineChannel(channelID, channelInfo);
        channels.TryAdd(channelID, channel);
        return channel;
    }

    public void OnPlayerLeft(int playerID)
    {
        var player = players[playerID];
        var channel = player.Channel;
        channel.Players.Remove(player);
        players.Remove(playerID);
        RemoveChannelIfNeeded(channel);
    }

    private void RemoveChannel(int channelID)
    {
        SafeGuard.Assert(channelID != ChannelInfo.PrivateChannelVirtualID);

        var channel = channels[channelID];
        SafeGuard.Assert(channel.Players.Count == 0);
        channels.Remove(channelID);
    }

    private void RemoveChannelIfNeeded(OnlineChannel channel)
    {
        if (channel.Players.Count == 0
            && SelfChannel != channel
            && channel.ID != ChannelInfo.MainChannelID
            && channel.ID != ChannelInfo.PrivateChannelVirtualID)
        {
            RemoveChannel(channel.ID);
        }
    }

    public void OnSelfChannelMove(int channelID, IEnumerable<PlayerPresenceDataWithID>? channelPlayers)
    {
        var target = GetChannel(channelID);
        var previous = Self.Channel;
        Self.Channel = target;

        foreach (var player in previous.Players)
            ClearPlayerPresenceInfo(player);

        if (channelPlayers is not null)
        {
            foreach (var info in channelPlayers)
                ApplyPlayerPresenceData(info);
        }

        if (previous != target)
        {
            if (previous.IsPrivate)
            {
                // we're leaving a private channel
                // move its remaining members into the virtual private channel
                var virtualChannel = GetChannel(ChannelInfo.PrivateChannelVirtualID);
                foreach (var player in previous.Players.ToArray())
                    MovePlayerToChannel(player, virtualChannel);
                RemoveChannel(previous.ID);
            }
            else
            {
                RemoveChannelIfNeeded(previous);
            }
        }

        // we're joining a private channel
        // move its members into the real private channel
        if (target.IsPrivate && channelPlayers is not null)
        {
            foreach (var info in channelPlayers)
                MovePlayerToChannel(GetPlayer(info.PlayerID), target);
        }
    }

    public void OnPlayerChannelMove(int playerID, int channelID, PlayerPresenceData? presence, out OnlinePlayer player)
    {
        player = GetPlayer(playerID);
        var previous = player.Channel;
        var current = GetChannel(channelID);

        MovePlayerToChannel(player, current);

        if (current != previous)
        {
            ClearPlayerPresenceInfo(player);
            RemoveChannelIfNeeded(previous);
        }

        if (presence is not null)
            ApplyPlayerPresenceData(player, presence.Value);
    }

    private void MovePlayerToChannel(OnlinePlayer player, OnlineChannel channel)
    {
        SafeGuard.Assert(players.ContainsValue(player));

        bool result = player.Channel.Players.Remove(player);
        SafeGuard.Assert(result);

        player.Channel = channel;
        channel.Players.Add(player);
    }

    private void ClearPlayerPresenceInfo(OnlinePlayer player)
    {
        SafeGuard.Assert(players.ContainsValue(player));

        player.Location = PlayerLocation.Empty;
        player.LastPing = -1;
        player.State = null;
        player.EpochBaselineState = null;
        player.PlayerEpoch = 0;
        player.LastPlayerSequence = 0;
        player.AwaitingPlayerKeyframe = false;
        player.GlobalFlags = PlayerGlobalFlags.None;
    }

    public void ApplyPlayerPresenceData(OnlinePlayer player, PlayerPresenceData info)
    {
        SafeGuard.Assert(players.ContainsValue(player));

        player.Location = info.Location;
        if (info.PlayerEpoch != player.PlayerEpoch)
        {
            player.State = null;
            player.EpochBaselineState = null;
            player.AwaitingPlayerKeyframe = false;
        }
        player.PlayerEpoch = info.PlayerEpoch;
        player.LastPlayerSequence = info.PlayerSequence;
        player.GlobalFlags = info.GlobalFlags;
    }

    public void ApplyPlayerPresenceData(PlayerPresenceDataWithID info)
        => ApplyPlayerPresenceData(info.PlayerID, info.Data);

    public void ApplyPlayerPresenceData(int playerID, PlayerPresenceData info)
        => ApplyPlayerPresenceData(GetPlayer(playerID), info);

    public void ApplyPlayerMovedInitialData(OnlinePlayer player, PlayerMovedInitialData data)
    {
        SafeGuard.Assert(players.ContainsValue(player));

        player.State = data.InitialState;
        player.EpochBaselineState = data.InitialState.Clone();
        player.PlayerEpoch = data.PlayerEpoch;
        player.LastPlayerSequence = data.PlayerSequence;
        player.AwaitingPlayerKeyframe = false;
    }

    public void ApplyPlayerMovedInitialData(PlayerMovedInitialDataWithID data)
        => ApplyPlayerMovedInitialData(data.PlayerID, data.InitialData);

    public void ApplyPlayerMovedInitialData(int playerID, PlayerMovedInitialData data)
        => ApplyPlayerMovedInitialData(GetPlayer(playerID), data);

    public bool TryGetPlayer(int playerID, [NotNullWhen(true)] out OnlinePlayer? player)
        => players.TryGetValue(playerID, out player);

    public OnlinePlayer GetPlayer(int playerID)
    {
        if (players.TryGetValue(playerID, out var player))
            return player;
        throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, SR.PlayerNotFound, playerID));
    }

    public bool TryGetPlayerOrSelf(int playerID, [NotNullWhen(true)] out OnlinePlayer? player)
    {
        if (players.TryGetValue(playerID, out player))
            return true;

        if (Self.ID == playerID)
        {
            player = Self;
            return true;
        }
        player = null;
        return false;
    }

    public OnlineChannel GetChannel(int channelID)
        => channels[channelID];

    public OnlinePlayer GetPlayerOrSelf(int playerID)
    {
        if (players.TryGetValue(playerID, out var player))
            return player;
        if (Self.ID == playerID)
            return Self;
        throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, SR.PlayerNotFound, playerID));
    }

    public PlayerLocation.ChangeResult OnPlayerLocationChanged(PlayerLocation location)
    {
        PlayerLocation.ChangeResult result = Self.Location.GetChangeResult(location);
        Self.Location = location;
        if (result != PlayerLocation.ChangeResult.None)
            SelfLocationChanged?.Invoke();
        return result;
    }
}
