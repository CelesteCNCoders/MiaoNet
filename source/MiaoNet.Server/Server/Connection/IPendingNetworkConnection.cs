namespace MiaoNet.Server;

/// <summary>
/// A "pending" connection, note that dispose this
/// will also dispose the completed "not pending" connection.
/// </summary>
public interface IPendingNetworkConnection : IDisposable
{
    public string RemoteAddress { get; }

    public Task<INetworkConnection> CompleteAsync(CancellationToken cancellationToken);
}
