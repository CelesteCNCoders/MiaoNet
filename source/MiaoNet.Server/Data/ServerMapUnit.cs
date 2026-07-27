using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerMapUnit
{
    private ImmutableHashSet<MiaoClientConnection> players;

    public ReaderWriterLockSlim StateLock { get; }

    public PlayerMap Map { get; }

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    public ServerMapUnit(PlayerMap map, MiaoClientConnection connection)
    {
        players = ImmutableHashSet<MiaoClientConnection>.Empty.Add(connection);
        Map = map;
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

    public IReadOnlyCollection<PlayerMovedInitialData> GetPlayerMovedInitialDatas(MiaoClientConnection except)
    {
        Debug.Assert(StateLock.IsWriteLockHeld);
        return (from con in players
                where con != except
                let p = con.Player
                select new PlayerMovedInitialData(
                    p.ID,
                    p.State!.Clone()
                )).ToList();
    }
}
