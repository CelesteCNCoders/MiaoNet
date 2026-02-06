using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ClientState
{
    private readonly Dictionary<int, OnlinePlayer> players;
    private readonly Dictionary<int, OnlineChannel> channels;

    public IReadOnlyDictionary<int, OnlinePlayer> Players => players;

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
            channels.Add(channel.ID, new OnlineChannel(channel.ID, channel.Name));
        foreach (var player in clientInitial.Players)
        {
            var p = AddNewPlayer(player.ChannelID, player.PlayerInfo, player.GlobalFlags);
            p.Location = player.Location;
        }
        Self = new(channels[clientInitial.ChannelID], clientInitial.SelfPlayerInfo, PlayerGlobalFlags.None);
    }

    public OnlinePlayer OnNewPlayerJoined(int channelID, PlayerInfo playerInfo, PlayerGlobalFlags globalFlags)
        => AddNewPlayer(channelID, playerInfo, globalFlags);

    private OnlinePlayer AddNewPlayer(int channelID, PlayerInfo playerInfo, PlayerGlobalFlags globalFlags)
    {
        var channel = channels[channelID];
        var player = new OnlinePlayer(channel, playerInfo, globalFlags);
        players.Add(player.ID, player);
        channel.Players.Add(player.ID, player);
        return player;
    }

    public void OnPlayerLeft(int playerID)
    {
        var player = players[playerID];
        var channel = player.Channel;
        channel.Players.Remove(playerID);
        players.Remove(playerID);
    }

    public bool TryGetPlayer(int playerID, [NotNullWhen(true)] out OnlinePlayer? player)
        => players.TryGetValue(playerID, out player);

    public OnlinePlayer GetPlayer(int playerID)
    {
        if (players.TryGetValue(playerID, out var player))
            return player;
        throw new KeyNotFoundException(string.Format(SR.PlayerNotFound, playerID));
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

    public OnlinePlayer GetPlayerOrSelf(int playerID)
    {
        if (players.TryGetValue(playerID, out var player))
            return player;
        if (Self.ID == playerID)
            return Self;
        throw new KeyNotFoundException(string.Format(SR.PlayerNotFound, playerID));
    }

    public PlayerLocation.ChangeResult OnPlayerLocationChanged(PlayerLocation location)
    {
        PlayerLocation.ChangeResult result = Self.Location.CompareTo(location);
        Self.Location = location;
        if (result != PlayerLocation.ChangeResult.None)
            SelfLocationChanged?.Invoke();
        return result;
    }
}