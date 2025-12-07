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

    public ClientState(PacketClientInitial clientInitial, PlayerLocation selfLocationInfo)
    {
        players = new();
        channels = new();

        foreach (var channel in clientInitial.Channels)
            channels.Add(channel.ID, new OnlineChannel(channel.ID, channel.Name));
        foreach (var player in clientInitial.Players)
            OnNewPlayerJoined(player);
        Self = new(channels[clientInitial.ChannelID], clientInitial.SelfPlayerInfo, selfLocationInfo);
    }

    public OnlinePlayer OnNewPlayerJoined(PacketPlayerJoined packet)
    {
        var channel = channels[packet.Info.ChannelID];
        var player = new OnlinePlayer(
            channel,
            packet.Info.Info,
            packet.Info.LocationInfo,
            packet.InitialState,
            packet.GraphicsInfo
        );
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

    public PlayerLocation.ChangeResult OnPlayerLocationChanged(PlayerLocation location)
    {
        PlayerLocation.ChangeResult result = Self.Location.CompareTo(location);
        Self.Location = location;
        return result;
    }
}