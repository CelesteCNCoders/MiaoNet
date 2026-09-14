using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MiaoNet.Server;
using MiaoNet.Shared;
using Microsoft.Extensions.Time.Testing;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class TlsTcpPendingConnectionTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private static readonly IMiaoCertificateService CertificateService = new TestCertificateService();

    [TestMethod]
    public async Task CompleteAsyncTransfersOwnershipSoDisposingPendingKeepsConnectionAlive()
    {
        var (serverSocket, clientSocket) = await CreateSocketPairAsync();
        var pending = new TlsTcpPendingConnection(CertificateService, serverSocket);

        NetworkStream clientStream = new(clientSocket, ownsSocket: true);
        await clientStream.WriteAsync(Connection.HandshakeHead, TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken);
        SslStream clientSslStream = new(clientStream, false, (_, _, _, _) => true);
        Task clientHandshake = clientSslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = "localhost" },
            TestContext.CancellationToken
        );

        INetworkConnection? connection = await pending.CompleteAsync(CancellationToken.None)
            .WaitAsync(WaitTimeout, TestContext.CancellationToken);
        await clientHandshake.WaitAsync(WaitTimeout, TestContext.CancellationToken);

        Assert.IsNotNull(connection);
        Assert.IsFalse(string.IsNullOrEmpty(connection.RemoteAddress));

        // the whole point: the pending connection no longer owns anything,
        // so disposing it must not kill the connection it handed over
        pending.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => pending.CompleteAsync(CancellationToken.None)
        );

        byte[] payload = [1, 2, 3];
        await clientSslStream.WriteAsync(payload, TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken);
        byte[] buffer = new byte[payload.Length];
        await connection.Stream.ReadExactlyAsync(buffer, TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken);
        CollectionAssert.AreEqual(payload, buffer);

        connection.Dispose();
        clientSslStream.Dispose();
    }

    [TestMethod]
    public async Task CompleteAsyncReturnsNullAndClosesSocketWhenHandshakeHeadMismatch()
    {
        var (serverSocket, clientSocket) = await CreateSocketPairAsync();
        var pending = new TlsTcpPendingConnection(CertificateService, serverSocket);

        NetworkStream clientStream = new(clientSocket, ownsSocket: true);
        await clientStream.WriteAsync(new byte[Connection.HandshakeHeadLength], TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken);

        // we must not be the ones closing it: the pending connection releases everything itself
        Assert.IsNull(await pending.CompleteAsync(CancellationToken.None)
            .WaitAsync(WaitTimeout, TestContext.CancellationToken));

        Assert.AreEqual(0, await clientStream.ReadAsync(new byte[1], TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken));

        // completing consumed it, disposing is just a no-op
        pending.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => pending.CompleteAsync(CancellationToken.None)
        );
    }

    [TestMethod]
    public async Task CompleteAsyncReleasesSocketWhenCancelled()
    {
        var (serverSocket, clientSocket) = await CreateSocketPairAsync();
        var pending = new TlsTcpPendingConnection(CertificateService, serverSocket);

        FakeTimeProvider timeProvider = new();
        using CancellationTokenSource cts = new(HandshakeTimeout, timeProvider);

        Task<INetworkConnection?> completing = pending.CompleteAsync(cts.Token);
        Assert.IsFalse(completing.IsCompleted);

        timeProvider.Advance(HandshakeTimeout);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => completing.WaitAsync(WaitTimeout, TestContext.CancellationToken)
        );

        NetworkStream clientStream = new(clientSocket, ownsSocket: true);
        Assert.AreEqual(0, await clientStream.ReadAsync(new byte[1], TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken));

        pending.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => pending.CompleteAsync(CancellationToken.None)
        );
    }

    [TestMethod]
    public async Task DisposeWithoutCompletionClosesSocketAndIsIdempotent()
    {
        var (serverSocket, clientSocket) = await CreateSocketPairAsync();
        var pending = new TlsTcpPendingConnection(CertificateService, serverSocket);

        pending.Dispose();
        pending.Dispose();

        Assert.IsFalse(string.IsNullOrEmpty(pending.RemoteAddress));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => pending.CompleteAsync(CancellationToken.None)
        );

        NetworkStream clientStream = new(clientSocket, ownsSocket: true);
        Assert.AreEqual(0, await clientStream.ReadAsync(new byte[1], TestContext.CancellationToken)
            .AsTask()
            .WaitAsync(WaitTimeout, TestContext.CancellationToken));
    }

    private static async Task<(Socket Server, Socket Client)> CreateSocketPairAsync()
    {
        using Socket listener = new(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);

        Socket clientSocket = new(SocketType.Stream, ProtocolType.Tcp);
        Task connectTask = clientSocket.ConnectAsync(listener.LocalEndPoint!);
        Socket serverSocket = await listener.AcceptAsync().WaitAsync(WaitTimeout);
        await connectTask.WaitAsync(WaitTimeout);
        return (serverSocket, clientSocket);
    }

    private sealed class TestCertificateService : IMiaoCertificateService
    {
        private readonly X509Certificate2 certificate;

        public TestCertificateService()
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new(
                "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1
            );
            SubjectAlternativeNameBuilder sanBuilder = new();
            sanBuilder.AddDnsName("localhost");
            request.CertificateExtensions.Add(sanBuilder.Build());
            using X509Certificate2 ephemeral = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1)
            );
            certificate = X509CertificateLoader.LoadPkcs12(
                ephemeral.Export(X509ContentType.Pkcs12), null
            );
        }

        public X509Certificate2 GetCertificate() => certificate;
    }

    public TestContext TestContext { get; set; }
}
