using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ClientState
{
    private readonly Dictionary<int, OnlinePlayer> players;
    private readonly Dictionary<int, OnlineChannel> channels;

    public IReadOnlyDictionary<int, OnlinePlayer> Players => players;

    public IReadOnlyDictionary<int, OnlineChannel> Channels => channels;

    public OnlinePlayer Self { get; private set; }

    public OnlineChannel SelfChannel { get; private set; }

    public ClientState(PacketClientInitial clientInitial, PlayerLocationInfo locationInfo)
    {
        players = new();
        channels = new();

        foreach (var channel in clientInitial.Channels)
        {
            channels.Add(channel.ID, new OnlineChannel(channel.ID, channel.Name));
        }
        foreach (var player in clientInitial.Players)
        {
            OnlinePlayer onlinePlayer;
            var channel = channels[player.ChannelID];
            players.Add(player.Info.ID, onlinePlayer = new OnlinePlayer(channel, player.Info, player.LocationInfo));
            channel.Players.Add(player.Info.ID, onlinePlayer);
        }
        Self = new(SelfChannel = channels[clientInitial.ChannelID], clientInitial.SelfPlayerInfo, locationInfo);
    }

    public OnlinePlayer OnNewPlayerJoined(ChannelPlayerLocationInfo info)
    {
        var channel = channels[info.ChannelID];
        var player = new OnlinePlayer(channel, info.Info, info.LocationInfo);
        players.Add(player.ID, player);
        player.Channel.Players.Add(player.ID, player);
        return player;
    }

    public void OnPlayerLeft(int playerID)
    {
        var player = players[playerID];
        var channel = player.Channel;
        channel.Players.Remove(playerID);
        players.Remove(playerID);
    }
}