using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerChannel
{
    private ImmutableHashSet<MiaoClientConnection> players;
    private ImmutableDictionary<PlayerMap, ServerMapUnit> mapUnits;

    public int ID { get; }

    public ChannelInfo Info { get; }

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    public ImmutableDictionary<PlayerMap, ServerMapUnit> MapUnits => mapUnits;

    public ServerChannel(int id, ChannelInfo info)
    {
        ID = id;
        Info = info;
        players = ImmutableHashSet<MiaoClientConnection>.Empty;
        mapUnits = ImmutableDictionary<PlayerMap, ServerMapUnit>.Empty;
    }

    public void OnAddPlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Add(c), connection);
        Debug.Assert(result);

        var map = connection.Player.Location.Map;
        OnPlayerMapMoveTo(connection, map);
    }

    public void OnPlayerMapMove(MiaoClientConnection connection, PlayerMap from, PlayerMap to)
    {
        OnPlayerMapMoveFrom(connection, from);
        OnPlayerMapMoveTo(connection, to);
    }

    private void OnPlayerMapMoveFrom(MiaoClientConnection connection, PlayerMap from)
    {
        if (from.IsEmpty)
            return;

        var omu = mapUnits[from];
        omu.OnRemovePlayer(connection);
        if (omu.Players.IsEmpty)
        {
            bool result = ImmutableInterlocked.Update(ref mapUnits, (d, u) => d.Remove(u.Map), omu);
            Debug.Assert(result);
        }
    }

    private void OnPlayerMapMoveTo(MiaoClientConnection connection, PlayerMap to)
    {
        if (to.IsEmpty)
            return;

        if (mapUnits.TryGetValue(to, out var unit))
        {
            unit.OnAddPlayer(connection);
        }
        else
        {
            ServerMapUnit unitNew = new(to, connection);
            bool result = ImmutableInterlocked.Update(
                ref mapUnits,
                (d, c) => d.Add(c.unitNew.Map, c.unitNew),
                (connection, unitNew)
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