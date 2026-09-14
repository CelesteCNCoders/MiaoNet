using System.Buffers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class TlsTcpPendingConnection : IPendingNetworkConnection
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    private readonly IMiaoCertificateService certificateService;

    private Socket? socket;

    public string RemoteAddress { get; }

    public TlsTcpPendingConnection(IMiaoCertificateService certificateService, Socket socket)
    {
        this.certificateService = certificateService;
        this.socket = socket;
        RemoteAddress = socket.RemoteEndPoint!.ToString()!;
    }

    public async Task<INetworkConnection?> CompleteAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(socket is null, this);

        Socket acceptedSocket = socket!;
        // from here on the socket belongs to networkStream, so disposing the stream is enough
        NetworkStream networkStream = new(acceptedSocket, ownsSocket: true);
        SslStream? sslStream = null;
        bool transferred = false;
        try
        {
            var buffer = pool.Rent(Connection.HandshakeHeadLength);
            try
            {
                var memory = buffer.AsMemory(0, Connection.HandshakeHeadLength);
                await networkStream.ReadExactlyAsync(memory, token);
                if (!memory.Span.SequenceEqual(Connection.HandshakeHead.Span))
                    return null;
            }
            finally
            {
                pool.Return(buffer);
            }

            // and from here on networkStream belongs to sslStream
            sslStream = new SslStream(networkStream);
            SslServerAuthenticationOptions options = new()
            {
                ServerCertificate = certificateService.GetCertificate(),
                EnabledSslProtocols = Connection.AllowedSslProtocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };
            await sslStream.AuthenticateAsServerAsync(options, token);

            var connection = new TlsTcpConnection(acceptedSocket, sslStream);
            transferred = true;
            return connection;
        }
        finally
        {
            socket = null;
            if (!transferred)
            {
                // the peer was rejected or something failed before anything was handed
                // over, so release everything here; only sslStream can still be null
                sslStream?.Dispose();
                networkStream.Dispose();
            }
        }
    }

    public void Dispose()
    {
        socket?.Dispose();
        socket = null;
    }
}
