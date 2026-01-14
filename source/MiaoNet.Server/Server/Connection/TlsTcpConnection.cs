using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;

namespace MiaoNet.Server;

public sealed class TlsTcpConnection : INetworkConnection
{
    private readonly Socket socket;
    private readonly SslStream sslStream;

    public string RemoteAddress => socket.RemoteEndPoint!.ToString()!;

    public Stream Stream => sslStream;

    public TlsTcpConnection(Socket socket, SslStream sslStream)
    {
        Debug.Assert(sslStream.IsAuthenticated);
        this.socket = socket;
        this.sslStream = sslStream;
    }

    public void Dispose()
    {
        sslStream.Dispose();
        socket.Dispose();
    }

    public void Shutdown()
    {
        socket.Shutdown(SocketShutdown.Both);
    }
}