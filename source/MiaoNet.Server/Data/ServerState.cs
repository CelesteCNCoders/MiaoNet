using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

[DebuggerDisplay("Players count = {players.Count}, Channels count = {channels.Count}")]
public sealed class ServerState : IPlayerScope
{
    private int nextPlayerID;
    private int nextChannelID;

    private ImmutableDictionary<int, MiaoClientConnection> players;
    private ImmutableDictionary<int, ServerChannel> channels;

    public ImmutableDictionary<int, MiaoClientConnection> Players => players;

    IEnumerable<MiaoClientConnection> IPlayerScope.Players => players.Values;

    public ImmutableDictionary<int, ServerChannel> Channels => channels;

    public ServerState()
    {
        nextPlayerID = nextChannelID = 0;

        players = ImmutableDictionary<int, MiaoClientConnection>.Empty;
        channels = ImmutableDictionary<int, ServerChannel>.Empty
            .Add(0, new ServerChannel(0, new ChannelInfo("main")));
    }

    public ServerPlayer CreateNewPlayer(PlayerInfo playerInfo)
    {
        int id = Interlocked.Increment(ref nextPlayerID);
        ServerChannel channel = channels[0];
        ServerPlayer player = new(channel, id, playerInfo);
        return player;
    }

    public ServerChannel CreateNewChannel(ChannelInfo channelInfo)
    {
        int id = Interlocked.Increment(ref nextChannelID);
        ServerChannel channel = new(id, channelInfo);
        return channel;
    }

    public void AddPlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Add(c.ID, c), connection);
        Debug.Assert(result);
        connection.Player.Channel.OnAddPlayer(connection);
    }

    public void AddChannel(ServerChannel channel)
    {
        bool result = ImmutableInterlocked.Update(ref channels, (d, c) => d.Add(c.ID, c), channel);
        Debug.Assert(result);
    }

    public void RemovePlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Remove(c.ID), connection);
        Debug.Assert(result);
        connection.Player.Channel.OnRemovePlayer(connection);
        RemoveChannelIfEmpty(connection.Player.Channel);
    }

    public MoveResult PlayerChannelMove(MiaoClientConnection connection, ServerChannel to)
    {
        var fromScope = connection.Player.Scope;
        var from = connection.Player.Channel;
        Debug.Assert(channels.ContainsValue(from));
        Debug.Assert(channels.ContainsValue(to));
        
        from.OnRemovePlayer(connection);
        connection.Player.Scope.Channel = to;
        to.OnAddPlayer(connection);
        RemoveChannelIfEmpty(from);
        
        return new MoveResult(fromScope, connection.Player.Scope);
    }

    public MoveResult PlayerMapMove(MiaoClientConnection connection, PlayerMapLocation to)
        => connection.Player.Channel.OnPlayerMapMove(connection, to);

    private void RemoveChannelIfEmpty(ServerChannel channel)
    {
        if (channel.Players.Count == 0 && channel.ID != 0)
        {
            bool result = ImmutableInterlocked.Update(ref channels, (d, c) => d.Remove(c.ID), channel);
            Debug.Assert(result);
        }
    }
}