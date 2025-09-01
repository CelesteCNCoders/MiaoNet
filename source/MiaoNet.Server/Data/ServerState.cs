using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

// TODO make the entire class immutable?
public sealed class ServerState
{
    public readonly record struct Client(ServerPlayer Player, MiaoClientConnection Connection);

    // used when moving player to another channel
    private readonly object lockObject = new();

    private int nextPlayerID;
    private int nextChannelID;
    private ImmutableDictionary<int, Client> allPlayers;
    private ImmutableDictionary<int, ServerChannel> allChannels;

    public ImmutableDictionary<int, Client> AllPlayers { get => allPlayers; set => allPlayers = value; }

    public ImmutableDictionary<int, ServerChannel> AllChannels { get => allChannels; set => allChannels = value; }

    public ServerState()
    {
        allPlayers = ImmutableDictionary<int, Client>.Empty;
        allChannels = ImmutableDictionary<int, ServerChannel>.Empty.Add(0, new ServerChannel(new ChannelStateInfo(0, "main")));
        nextPlayerID = nextChannelID = 1;
    }

    public ServerPlayer CreateNewPlayer(HandshakeData handshakeData)
    {
        int id = Interlocked.Increment(ref nextPlayerID);
        ServerChannel channel = AllChannels[0];
        ServerPlayer player = new(channel, new(id, handshakeData.Name), new(string.Empty, string.Empty));
        return player;
    }

    public ServerChannel CreateNewChannel(string channelName)
    {
        int id = Interlocked.Increment(ref nextChannelID);
        ServerChannel channel = new(new ChannelStateInfo(id, channelName));
        return channel;
    }

    public void AddPlayer(ServerPlayer player, MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref allPlayers, d => d.SetItem(player.ID, new(player, connection)));
        Debug.Assert(result);
        player.Channel.OnAddPlayer(player, connection);
    }

    public void AddChannel(ServerChannel channel)
    {
        bool result = ImmutableInterlocked.Update(ref allChannels, d => d.SetItem(channel.ID, channel));
        Debug.Assert(result);
    }

    public void RemovePlayer(ServerPlayer player)
    {
        bool result = ImmutableInterlocked.Update(ref allPlayers, d => d.Remove(player.ID));
        Debug.Assert(result);
        player.Channel.OnRemovePlayer(player);
    }

    public void RemoveChannel(ServerChannel channel)
    {
        // TODO
        throw new NotImplementedException();
    }
}