using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerChannel : IPlayerScope
{
    private ImmutableHashSet<MiaoClientConnection> players;
    private ImmutableDictionary<PlayerMapLocation, ServerMap> maps;

    public int ID { get; }

    public ChannelInfo Info { get; }

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    IEnumerable<MiaoClientConnection> IPlayerScope.Players => players;

    public IEnumerable<MiaoClientConnection> AllPlayers => players;

    public ImmutableDictionary<PlayerMapLocation, ServerMap> Maps => maps;

    public ServerChannel(int id, ChannelInfo info)
    {
        ID = id;
        Info = info;
        players = ImmutableHashSet<MiaoClientConnection>.Empty;
        maps = ImmutableDictionary<PlayerMapLocation, ServerMap>.Empty;
    }

    public void OnAddPlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Add(c), connection);
        Debug.Assert(result);

        var mapLoc = connection.Player.Location.Map;
        var mapScope = OnPlayerMapMoveTo(connection, mapLoc);
        connection.Player.Scope = new ScopeTuple(this, mapScope);
    }

    public MoveResult OnPlayerMapMove(MiaoClientConnection connection, PlayerMapLocation to)
    {
        var from = connection.Player.Location.Map;
        var fromScope = connection.Player.Scope;
        OnPlayerMapMoveFrom(connection, from);
        var mapScope = OnPlayerMapMoveTo(connection, to);
        connection.Player.Scope.Map = mapScope;
        return new MoveResult(fromScope, connection.Player.Scope);
    }

    private void OnPlayerMapMoveFrom(MiaoClientConnection connection, PlayerMapLocation from)
    {
        if (from.IsEmpty)
            return;

        var oldMap = maps[from];
        oldMap.OnRemovePlayer(connection);
        if (oldMap.Players.IsEmpty)
        {
            bool result = ImmutableInterlocked.Update(ref maps, (d, u) => d.Remove(u.MapLocation), oldMap);
            Debug.Assert(result);
            oldMap.Dispose();
        }
    }

    private ServerMap? OnPlayerMapMoveTo(MiaoClientConnection connection, PlayerMapLocation to)
    {
        if (to.IsEmpty)
            return null;

        if (maps.TryGetValue(to, out var map))
        {
            map.OnAddPlayer(connection);
        }
        else
        {
            map = new ServerMap(to, connection);
            bool result = ImmutableInterlocked.Update(
                ref maps,
                (d, c) => d.Add(c.map.MapLocation, c.map),
                (connection, map)
            );
            Debug.Assert(result);
        }
        return map;
    }

    public void OnRemovePlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Remove(c), connection);
        Debug.Assert(result);

        var mapLoc = connection.Player.Location.Map;
        OnPlayerMapMoveFrom(connection, mapLoc);
    }
}
