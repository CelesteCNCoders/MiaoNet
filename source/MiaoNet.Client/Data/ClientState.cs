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

        foreach (var channel in clientInitial.Channels)
            channels.Add(channel.ID, new OnlineChannel(channel.ID, channel.ChannelInfo));
        foreach (var player in clientInitial.Players)
        {
            var p = AddNewPlayer(player.ChannelID, player.PlayerID, player.PlayerInfo, player.GlobalFlags);
            p.Location = player.Location;
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
        channels.Add(channelID, channel);
        return channel;
    }

    public void OnPlayerLeft(int playerID)
    {
        var player = players[playerID];
        var channel = player.Channel;
        channel.Players.Remove(player);
        players.Remove(playerID);
    }

    public void OnChannelRemoved(int channelID)
    {
        var channel = channels[channelID];
        SafeGuard.Assert(channel.Players.Count == 0);
        channels.Remove(channelID);
    }

    public void OnSelfChannelMove(int channelID, out OnlineChannel previous, out OnlineChannel current)
    {
        var c = GetChannel(channelID);
        previous = Self.Channel;
        Self.Channel = c;
        current = c;

        if (previous != current)
        {
            foreach (var player in previous.Players)
                ClearPlayerPresenceInfo(player);
        }
        return;
    }

    public void OnPlayerChannelMove(int playerID, int channelID, out OnlinePlayer player, out OnlineChannel previous, out OnlineChannel current)
    {
        player = GetPlayer(playerID);
        previous = player.Channel;
        current = GetChannel(channelID);

        bool result = player.Channel.Players.Remove(player);
        SafeGuard.Assert(result);
        player.Channel = current;
        current.Players.Add(player);

        if (current != Self.Channel)
            ClearPlayerPresenceInfo(player);

        return;
    }

    private static void ClearPlayerPresenceInfo(OnlinePlayer player)
    {
        player.Location = PlayerLocation.Empty;
        player.LastPing = -1;
        player.State = null;
        player.GlobalFlags = PlayerGlobalFlags.None;
    }

    public void ApplyPlayerPresenceData(PlayerPresenceDataWithID info)
        => ApplyPlayerPresenceData(info.PlayerID, info.Data);

    public void ApplyPlayerPresenceData(int playerID, PlayerPresenceData info)
    {
        var player = GetPlayer(playerID);
        player.Location = info.Location;
        player.GlobalFlags = info.GlobalFlags;
    }

    public void ApplyPlayerMovedInitialData(PlayerMovedInitialDataWithID data)
        => ApplyPlayerMovedInitialData(data.PlayerID, data.InitialData);

    public void ApplyPlayerMovedInitialData(int playerID, PlayerMovedInitialData data)
    {
        var player = GetPlayer(playerID);
        player.State = data.InitialState;
    }

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