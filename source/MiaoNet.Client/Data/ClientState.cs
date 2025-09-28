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
            var channel = channels[player.Info.ChannelID];
            onlinePlayer = new OnlinePlayer(channel, player.Info.Info, player.Info.LocationInfo);
            onlinePlayer.State = player.InitialState;
            onlinePlayer.GraphicsInfo = player.GraphicsInfo;
            players.Add(player.Info.Info.ID, onlinePlayer);
            channel.Players.Add(player.Info.Info.ID, onlinePlayer);
        }
        Self = new(SelfChannel = channels[clientInitial.ChannelID], clientInitial.SelfPlayerInfo, locationInfo);
    }

    public OnlinePlayer OnNewPlayerJoined(PacketPlayerJoined packet)
    {
        var channel = channels[packet.Info.ChannelID];
        var player = new OnlinePlayer(channel, packet.Info.Info, packet.Info.LocationInfo);
        player.GraphicsInfo = packet.GraphicsInfo;
        player.State = packet.InitialState;
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