using System.Diagnostics;
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
            var p = AddNewPlayer(player.ChannelID, player.PlayerInfo, player.OnlineStatus);
            p.Location = player.Location;
        }
        Self = new(channels[clientInitial.ChannelID], clientInitial.SelfPlayerInfo, PlayerOnlineStatus.Normal);
    }

    public OnlinePlayer OnNewPlayerJoined(int channelID, PlayerInfo playerInfo, PlayerOnlineStatus onlineStatus)
        => AddNewPlayer(channelID, playerInfo, onlineStatus);

    private OnlinePlayer AddNewPlayer(int channelID, PlayerInfo playerInfo, PlayerOnlineStatus onlineStatus)
    {
        var channel = channels[channelID];
        var player = new OnlinePlayer(channel, playerInfo, onlineStatus);
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

    public PlayerLocation.ChangeResult OnPlayerLocationChanged(PlayerLocation location)
    {
        PlayerLocation.ChangeResult result = Self.Location.CompareTo(location);
        Self.Location = location;
        if (result != PlayerLocation.ChangeResult.None)
            SelfLocationChanged?.Invoke();
        return result;
    }
}