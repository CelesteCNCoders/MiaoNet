namespace MiaoNet.Server;

public interface INetworkConnection : IDisposable
{
    public string RemoteAddress { get; }

    public Stream Stream { get; }

    public void Shutdown();
}