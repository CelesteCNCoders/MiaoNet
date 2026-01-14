using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class TlsTcpPendingConnection : IPendingNetworkConnection
{
    private readonly IMiaoCertificateService certificateService;
    private readonly Socket socket;

    public string RemoteAddress => socket.RemoteEndPoint!.ToString()!;

    public TlsTcpPendingConnection(IMiaoCertificateService certificateService, Socket socket)
    {
        this.certificateService = certificateService;
        this.socket = socket;
    }

    public async Task<INetworkConnection> CompleteAsync(CancellationToken token)
    {
        SslStream sslStream = new SslStream(new NetworkStream(socket));
        try
        {
            SslServerAuthenticationOptions options = new()
            {
                ServerCertificate = certificateService.GetCertificate(),
                EnabledSslProtocols = Connection.AllowedSslProtocols,
                CertificateRevocationCheckMode = X509RevocationMode.Online
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
