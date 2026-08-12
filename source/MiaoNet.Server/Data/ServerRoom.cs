using System.Collections.Immutable;

namespace MiaoNet.Server;

// TODO: wire up handler
public sealed class ServerRoom : IPlayerScope
{
    private ImmutableHashSet<MiaoClientConnection> players;

    public string RoomId { get; }

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    IEnumerable<MiaoClientConnection> IPlayerScope.Players => players;

    public IEnumerable<MiaoClientConnection> AllPlayers => players;

    public ServerRoom(string roomId)
    {
        RoomId = roomId;
        players = ImmutableHashSet<MiaoClientConnection>.Empty;
    }

    public void OnAddPlayer(MiaoClientConnection connection)
    {
        ImmutableInterlocked.Update(ref players, (d, c) => d.Add(c), connection);
    }

    public void OnRemovePlayer(MiaoClientConnection connection)
    {
        ImmutableInterlocked.Update(ref players, (d, c) => d.Remove(c), connection);
    }
}
