using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
            .Add(
                ChannelInfo.MainChannelID,
                new ServerChannel(ChannelInfo.MainChannelID, new ChannelInfo("main"))
            );
    }

    public ServerPlayer CreateNewPlayer(PlayerInfo playerInfo)
    {
        int id = Interlocked.Increment(ref nextPlayerID);
        ServerChannel channel = channels[ChannelInfo.MainChannelID];
        ServerPlayer player = new(channel, id, playerInfo);
        return player;
    }

    public ServerChannel CreateNewChannel(ChannelInfo channelInfo)
    {
        int id = Interlocked.Increment(ref nextChannelID);
        ServerChannel channel = new(id, channelInfo);
        return channel;
    }

    public bool TryGetChannelByName(string name, [NotNullWhen(true)] out ServerChannel? channel)
    {
        foreach (var c in channels.Values)
        {
            if (c.Info.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                channel = c;
                return true;
            }
        }
        channel = null;
        return false;
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
        TryRemoveEmptyChannel(connection.Player.Channel);
    }

    public void PlayerChannelMove(MiaoClientConnection connection, ServerChannel from, ServerChannel to)
    {
        Debug.Assert(channels.ContainsValue(from));
        Debug.Assert(channels.ContainsValue(to));

        from.OnRemovePlayer(connection);
        connection.Player.Channel = to;
        to.OnAddPlayer(connection);
        TryRemoveEmptyChannel(from);
    }

    /// <summary>
    /// Removes the channel if it is empty and not the always-retained main channel.
    /// Does nothing otherwise.
    /// </summary>
    private void TryRemoveEmptyChannel(ServerChannel channel)
    {
        if (channel.Players.Count == 0 && channel.ID != ChannelInfo.MainChannelID)
        {
            bool result = ImmutableInterlocked.Update(ref channels, (d, c) => d.Remove(c.ID), channel);
            Debug.Assert(result);
        }
    }
}