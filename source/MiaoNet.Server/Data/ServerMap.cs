using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerMap : IPlayerScope, IDisposable
{
    private ImmutableHashSet<MiaoClientConnection> players;
    private readonly Channel<Func<Task>> workQueue;
    private readonly Task consumer;

    public PlayerMapLocation MapLocation { get; }

    public ImmutableHashSet<MiaoClientConnection> Players => players;

    IEnumerable<MiaoClientConnection> IPlayerScope.Players => players;

    public ServerMap(PlayerMapLocation mapLocation, MiaoClientConnection connection)
    {
        players = ImmutableHashSet<MiaoClientConnection>.Empty.Add(connection);
        MapLocation = mapLocation;

        workQueue = Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });
        consumer = ConsumeAsync();
    }

    public ValueTask PostAsync(Func<Task> work)
        => workQueue.Writer.WriteAsync(work);

    public async Task<T> PostAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        await workQueue.Writer.WriteAsync(() =>
        {
            tcs.SetResult(work());
            return Task.CompletedTask;
        });
        return await tcs.Task;
    }

    private async Task ConsumeAsync()
    {
        await foreach (var work in workQueue.Reader.ReadAllAsync())
            await work();
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

    public Task<IReadOnlyCollection<PlayerMovedInitialDataWithID>> GetPlayerMovedInitialDatasAsync(MiaoClientConnection except)
    {
        return PostAsync(() =>
        {
            var list = new List<PlayerMovedInitialDataWithID>(players.Count);
            foreach (var con in players)
            {
                var p = con.Player;
                // players that in debug map can cause null state
                if (con == except || p.State is null)
                    continue;
                list.Add(new PlayerMovedInitialDataWithID(p.ID, new PlayerMovedInitialData(p.State!.Clone())));
            }
            return (IReadOnlyCollection<PlayerMovedInitialDataWithID>)list;
        });
    }

    public void Dispose()
    {
        workQueue.Writer.Complete();
        consumer.Wait();
    }
}
