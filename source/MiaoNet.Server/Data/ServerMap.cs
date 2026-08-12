using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerMap : IPlayerScope, IDisposable
{
    private ImmutableHashSet<MiaoClientConnection> players;

    public ReaderWriterLockSlim StateLock { get; }

    public PlayerMapLocation MapLocation { get; }

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    IEnumerable<MiaoClientConnection> IPlayerScope.Players => players;

    public ServerMap(PlayerMapLocation mapLocation, MiaoClientConnection connection)
    {
        players = ImmutableHashSet<MiaoClientConnection>.Empty.Add(connection);
        MapLocation = mapLocation;
        StateLock = new();
    }

    public void OnAddPlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Add(c), connection);
        Debug.Assert(result);
    }

    public void OnRemovePlayer(MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, (d, c) => d.Remove(c), connection);
        Debug.Assert(result);
    }

    public IReadOnlyCollection<PlayerMovedInitialDataWithID> GetPlayerMovedInitialDatas(MiaoClientConnection except)
    {
        Debug.Assert(StateLock.IsWriteLockHeld);

        var list = new List<PlayerMovedInitialDataWithID>(players.Count);
        foreach (var con in players)
        {
            var p = con.Player;
            // players that in debug map can cause null state
            if (con == except || p.State is null)
                continue;
            list.Add(new PlayerMovedInitialDataWithID(p.ID, new PlayerMovedInitialData(p.State!.Clone())));
        }
        return list;
    }

    public void Dispose()
    {
        StateLock.Dispose();
    }
}
