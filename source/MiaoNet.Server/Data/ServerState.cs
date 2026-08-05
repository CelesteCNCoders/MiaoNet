using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;
using MiaoNet.Server.GameScope;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

[DebuggerDisplay("Players count = {players.Count}, Channels count = {channels.Count}")]
public sealed class ServerState
{
    private int nextPlayerID;
    private int nextChannelID;
    private readonly ILogger scopeTreeLogger;

    private ImmutableDictionary<int, MiaoClientConnection> players;
    private ImmutableDictionary<int, ServerChannel> channels;
    private readonly ServerChannel mainChannel;

    public ImmutableDictionary<int, MiaoClientConnection> Players => players;

    public ImmutableDictionary<int, ServerChannel> Channels => channels;

    public ScopeTree ScopeTree { get; }

    public ServerState(ILogger<ScopeTree> scopeTreeLogger)
    {
        nextPlayerID = nextChannelID = 0;
        this.scopeTreeLogger = scopeTreeLogger;

        players = ImmutableDictionary<int, MiaoClientConnection>.Empty;
        channels = ImmutableDictionary<int, ServerChannel>.Empty;
        ScopeTree = new ScopeTree(scopeTreeLogger);
        mainChannel = CreateChannel(new ChannelInfo("main"));
    }

    public ServerPlayer CreateAndAddPlayer(PlayerInfo playerInfo)
    {
        int id = Interlocked.Increment(ref nextPlayerID);
        ServerPlayer player = new(id, playerInfo);
        ScopeTree.AddPlayer(player, mainChannel.Scope!);
        return player;
    }

    public void RegisterConnection(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Add(c.ID, c), connection);
        Debug.Assert(result);
    }

    public void RemovePlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Remove(c.ID), connection);
        Debug.Assert(result);
        ScopeTree.RemovePlayer(connection.Player);
    }

    public ServerChannel CreateChannel(ChannelInfo channelInfo)
    {
        int id = Interlocked.Increment(ref nextChannelID);
        ServerChannel channel = new(id, channelInfo);
        ScopeTree.AddChannel(channel);
        ImmutableInterlocked.Update(ref channels, (d, c) => d.Add(c.ID, c), channel);
        
        return channel;
    }

    public MoveResult MovePlayerToChannel(ServerPlayer player, ServerChannel channel)
        => ScopeTree.MovePlayerToChannel(player, channel);

    public MoveResult MovePlayerToMap(ServerPlayer player, PlayerMapLocation map)
        => ScopeTree.MovePlayerToMap(player, map);

    //TODO: Use Room
    // public MoveResult MovePlayerToRoom(ServerPlayer player, string room)
    // {
    // }
    
}