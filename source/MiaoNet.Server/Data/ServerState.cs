using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace MiaoNet.Server;

// TODO make the entire class immutable?
[DebuggerDisplay("Players count = {allPlayers.Count}, Channels count = {allChannels.Count}")]
public sealed class ServerState
{
    [DebuggerDisplay("{Player}")]
    public readonly record struct Client(ServerPlayer Player, MiaoClientConnection Connection);

    private readonly ReaderWriterLockSlim stateLock = new();

    private int currentPlayerID;
    private int currentChannelID;
    private ImmutableDictionary<int, Client> allPlayers;
    private ImmutableDictionary<int, ServerChannel> allChannels;

    public ImmutableDictionary<int, Client> AllPlayers => allPlayers;

    public ImmutableDictionary<int, ServerChannel> AllChannels => allChannels;

    public ReaderWriterLockSlim StateLock => stateLock;

    public ServerState()
    {
        allPlayers = ImmutableDictionary<int, Client>.Empty;
        allChannels = ImmutableDictionary<int, ServerChannel>.Empty.Add(0, new ServerChannel(new ChannelInfo(0, "main")));
        currentPlayerID = currentChannelID = 0;
    }

    public ServerPlayer CreateNewPlayer(HandshakeResult handshakeResult)
    {
        int id = Interlocked.Increment(ref currentPlayerID);
        ServerChannel channel = AllChannels[0];
        ServerPlayer player = new(channel, new(id, handshakeResult.HandshakeData.Name));
        return player;
    }

    public ServerChannel CreateNewChannel(string channelName)
    {
        int id = Interlocked.Increment(ref currentChannelID);
        ServerChannel channel = new(new ChannelInfo(id, channelName));
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