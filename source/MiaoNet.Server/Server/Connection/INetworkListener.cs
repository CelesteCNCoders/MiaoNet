namespace MiaoNet.Server;

public interface INetworkListener
{
    public void Listen();

    public Task<IPendingNetworkConnection> AcceptAsync(CancellationToken cancellationToken = default);
}
