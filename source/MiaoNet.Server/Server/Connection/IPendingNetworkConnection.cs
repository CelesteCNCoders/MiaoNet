namespace MiaoNet.Server;

public interface IPendingNetworkConnection : IDisposable
{
    public string RemoteAddress { get; }

    public Task<INetworkConnection?> CompleteAsync(CancellationToken cancellationToken);
}
