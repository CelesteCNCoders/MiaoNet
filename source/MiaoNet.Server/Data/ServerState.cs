using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace MiaoNet.Server;

[DebuggerDisplay("Players count = {players.Count}, Channels count = {channels.Count}")]
public sealed class ServerState
{
    private int nextPlayerID;
    private int nextChannelID;

    private ImmutableDictionary<int, MiaoClientConnection> players;
    private ImmutableDictionary<int, ServerChannel> channels;

    public ImmutableDictionary<int, MiaoClientConnection> Players => players;

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
        ServerPlayer player = new(0, id, playerInfo);
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
        channels[connection.Player.ChannelId].OnAddPlayer(connection);
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
        var channel = channels[connection.Player.ChannelId];
        channel.OnRemovePlayer(connection);
        RemoveChannelIfEmpty(channel);
    }

    public void PlayerChannelMove(MiaoClientConnection connection, ServerChannel from, ServerChannel to)
    {
        Debug.Assert(channels.ContainsValue(from));
        Debug.Assert(channels.ContainsValue(to));

        from.OnRemovePlayer(connection);
        connection.Player.ChannelId = to.ID;
        to.OnAddPlayer(connection);
        RemoveChannelIfEmpty(from);
    }

    private void RemoveChannelIfEmpty(ServerChannel channel)
    {
        if (channel.Players.Count == 0 && channel.ID != 0)
        {
            bool result = ImmutableInterlocked.Update(ref channels, (d, c) => d.Remove(c.ID), channel);
            Debug.Assert(result);
        }
    }
}