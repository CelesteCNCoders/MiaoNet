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

    public bool IsPrivate => Info.IsPrivate;

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    IEnumerable<MiaoClientConnection> IPlayerScope.Players => players;

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

        var map = connection.Player.Location.Map;
        OnPlayerMapMoveTo(connection, map);
    }

    public void OnPlayerMapMove(MiaoClientConnection connection, PlayerMapLocation from, PlayerMapLocation to)
    {
        OnPlayerMapMoveFrom(connection, from);
        OnPlayerMapMoveTo(connection, to);
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
        }
    }

    private void OnPlayerMapMoveTo(MiaoClientConnection connection, PlayerMapLocation to)
    {
        if (to.IsEmpty)
            return;

        if (maps.TryGetValue(to, out var map))
        {
            map.OnAddPlayer(connection);
        }
        else
        {
            ServerMap mapNew = new(to, connection);
            bool result = ImmutableInterlocked.Update(
                ref maps,
                (d, c) => d.Add(c.mapNew.MapLocation, c.mapNew),
                (connection, mapNew)
            );
            Debug.Assert(result);
        }
    }

    public void OnRemovePlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Remove(c), connection);
        Debug.Assert(result);

        var map = connection.Player.Location.Map;
        OnPlayerMapMoveFrom(connection, map);
    }
}