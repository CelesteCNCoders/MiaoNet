using System.Buffers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed class TlsTcpPendingConnection : IPendingNetworkConnection
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    private readonly IMiaoCertificateService certificateService;
    private readonly Socket socket;

    public string RemoteAddress => socket.RemoteEndPoint!.ToString()!;

    public TlsTcpPendingConnection(IMiaoCertificateService certificateService, Socket socket)
    {
        this.certificateService = certificateService;
        this.socket = socket;
    }

    public async Task<INetworkConnection?> CompleteAsync(CancellationToken token)
    {
        NetworkStream networkStream = new NetworkStream(socket);
        var buffer = pool.Rent(Connection.HandshakeHeadLength);
        try
        {
            var memory = buffer.AsMemory(0, Connection.HandshakeHeadLength);
            await networkStream.ReadExactlyAsync(memory, token);
            if (!memory.Span.SequenceEqual(Connection.HandshakeHead.Span))
            {
                networkStream.Dispose();
                socket.Dispose();
                return null;
            }
        }
        finally
        {
            pool.Return(buffer);
        }

        SslStream sslStream = new SslStream(networkStream);
        try
        {
            SslServerAuthenticationOptions options = new()
            {
                ServerCertificate = certificateService.GetCertificate(),
                EnabledSslProtocols = Connection.AllowedSslProtocols,
                // no need to check server-side
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            };
            await sslStream.AuthenticateAsServerAsync(options, token);

            return new TlsTcpConnection(socket, sslStream);
        }
        catch (Exception)
        {
            sslStream.Dispose();
            throw;
        }
    }

    public void Close()
    {
        socket.Shutdown(SocketShutdown.Both);
        socket.Close();
    }

    public void Dispose()
        => socket.Dispose();
}
