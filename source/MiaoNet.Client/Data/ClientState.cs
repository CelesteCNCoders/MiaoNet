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

    public ClientState(PacketClientInitial clientInitial)
    {
        players = new();
        channels = new();

        foreach (var channel in clientInitial.Channels)
            channels.Add(channel.ID, new OnlineChannel(channel.ID, channel.Name));
        foreach (var player in clientInitial.Players)
        {
            var p = AddNewPlayer(player.ChannelID, player.PlayerInfo);
            p.Location = player.Location;
            p.GraphicsInfo = player.GraphicsInfo;
            p.State = player.State;
        }
        Self = new(channels[clientInitial.ChannelID], clientInitial.SelfPlayerInfo);
    }

    public OnlinePlayer OnNewPlayerJoined(int channelID, PlayerInfo playerInfo)
        => AddNewPlayer(channelID, playerInfo);

    private OnlinePlayer AddNewPlayer(int channelID, PlayerInfo playerInfo)
    {
        var channel = channels[channelID];
        var player = new OnlinePlayer(channel, playerInfo);
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
        return result;
    }
}