using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MiaoNet.Shared;

namespace MiaoNet.Server.GameScope;

public sealed class MapScope : Scope<PlayerMap, string>
{
    private readonly Channel<Func<Task>> workQueue;
    private readonly Task consumer;
    private readonly ILogger logger;

    public PlayerMap Map { get; }

    public override bool Permanent => false;

    public MapScope(PlayerMap map, ChannelScope parent, ILogger logger) : base(map, parent)
    {
        this.logger = logger;
        Map = map;
        parent.AddChild(map, this);

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
        try
        {
            await foreach (var work in workQueue.Reader.ReadAllAsync())
                await work();
        }
        catch (Exception e)
        {
            logger.LogError(e, "MapScope consumer crashed for map {map}", Map);
        }
    }

    public override string ToString() => $"Map({Map})";
}