using MiaoNet.Server;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class MiaoClientConnectionTests
{
    [TestMethod]
    public async Task PendingRequestIsRemovedAfterTimeout()
    {
        var connection = CreateConnection();
        var request = new PacketPing();
        TaskCompletionSource timeoutCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await connection.RequestAsync(
            request,
            _ =>
            {
                Assert.Fail("A response was not supplied.");
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(20),
            () =>
            {
                timeoutCalled.TrySetResult();
                return Task.CompletedTask;
            }
        );

        await timeoutCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(connection.TakeResponseHandler(FirstRequestID));
    }

    [TestMethod]
    public async Task ResponseCancelsTimeoutHandler()
    {
        var connection = CreateConnection();
        var request = new PacketPing();
        bool timedOut = false;
        bool responded = false;

        await connection.RequestAsync(
            request,
            _ =>
            {
                responded = true;
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100),
            () =>
            {
                timedOut = true;
                return Task.CompletedTask;
            }
        );

        var handler = connection.TakeResponseHandler(FirstRequestID);
        Assert.IsNotNull(handler);
        await handler(new PacketPong());
        await Task.Delay(150);

        Assert.IsTrue(responded);
        Assert.IsFalse(timedOut);
    }

    [TestMethod]
    public async Task PendingRequestsHaveACap()
    {
        var connection = CreateConnection();
        using CancellationTokenSource cancellation = new();

        for (int i = 0; i < MiaoClientConnection.MaxPendingRequests; i++)
        {
            await connection.RequestAsync(
                new PacketPing(),
                _ => Task.CompletedTask,
                TimeSpan.FromMinutes(1),
                cancellationToken: cancellation.Token
            );
        }

        bool accepted = await connection.RequestAsync(
            new PacketPing(),
            _ => Task.CompletedTask,
            TimeSpan.FromMinutes(1),
            cancellationToken: cancellation.Token
        );

        Assert.IsFalse(accepted, "Requests beyond the cap should be rejected.");

        cancellation.Cancel();
    }

    [TestMethod]
    public void NetModIDOfADeclarationIsFoundByNameAndProtocolVersion()
    {
        var connection = CreateConnection();
        connection.SetNetMods([
            new NetModInfo("miao.test.a", 4),
            new NetModInfo("miao.test.b", 2)
        ]);

        Assert.IsTrue(connection.TryGetNetModID(new NetModInfo("miao.test.a", 4), out byte index));
        Assert.AreEqual(0, index);

        Assert.IsTrue(connection.TryGetNetModID(new NetModInfo("miao.test.b", 2), out index));
        Assert.AreEqual(1, index);
    }

    [TestMethod]
    public void NetModIDOfAnotherProtocolVersionOrNameIsNotFound()
    {
        var connection = CreateConnection();
        Assert.IsFalse(connection.TryGetNetModID(
            new NetModInfo("miao.test.a", 1), out _
        ), "A connection that declared nothing has no index to give.");

        connection.SetNetMods([new NetModInfo("miao.test.a", 1)]);

        Assert.IsFalse(connection.TryGetNetModID(new NetModInfo("miao.test.a", 2), out _));
        Assert.IsFalse(connection.TryGetNetModID(new NetModInfo("miao.test.other", 1), out _));
    }

    [TestMethod]
    public void ADeclarationThatCannotBeIndexedIsRejected()
    {
        var connection = CreateConnection();

        // An index is one byte wide, so a declaration longer than that cannot be addressed.
        NetModInfo[] tooMany = new NetModInfo[NetModInfo.MaxNetMods + 1];
        for (int i = 0; i < tooMany.Length; i++)
            tooMany[i] = new NetModInfo($"miao.test.{i}", 1);

        Assert.ThrowsExactly<InvalidDataException>(() => connection.SetNetMods(tooMany));

        Assert.ThrowsExactly<InvalidDataException>(() => connection.SetNetMods([
            new NetModInfo(string.Empty, 1)
        ]));

        Assert.IsEmpty(connection.NetMods);
    }

    private const int FirstRequestID = 1;

    private static MiaoClientConnection CreateConnection()
    {
        ServerChannel channel = new(0, new ChannelInfo("test"));
        ServerPlayer player = new(channel, 1, new PlayerInfo(1, "test", string.Empty, string.Empty, Color.White));
        return new MiaoClientConnection(
            new FakeNetworkConnection(),
            player,
            NullLogger<MiaoClientConnection>.Instance,
            null!,
            new MiaoMetricsService()
        );
    }

    private sealed class FakeNetworkConnection : INetworkConnection
    {
        public string RemoteAddress => "test";
        public Stream Stream { get; } = new MemoryStream();
        public void Shutdown() { }
        public void Dispose() => Stream.Dispose();
    }
}
