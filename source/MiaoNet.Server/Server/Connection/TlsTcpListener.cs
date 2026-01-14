using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MiaoNet.Server;

public sealed class TlsTcpListener : INetworkListener
{
    private readonly IMiaoCertificateService certificateService;
    private readonly Socket socket;

    public TlsTcpListener(IMiaoCertificateService certificateService, EndPoint listenEndPoint)
    {
        this.certificateService = certificateService;
        socket = new(SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(listenEndPoint);
    }

    public void Listen()
        => socket.Listen();

    public async Task<IPendingNetworkConnection> AcceptAsync(CancellationToken token = default)
    {
        var acceptedSocket = await socket.AcceptAsync(token);
        acceptedSocket.NoDelay = true;
        return new TlsTcpPendingConnection(certificateService, acceptedSocket);
    }
}
