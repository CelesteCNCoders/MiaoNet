using Microsoft.Extensions.Logging.Abstractions;
using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class MiaoClientConnectionTests
{
    [TestMethod]
    public void OutgoingQueueIsBounded()
    {
        var connection = CreateConnection();
        for (int i = 0; i < MiaoClientConnection.PacketChannelSize; i++)
            Assert.IsTrue(connection.TryQueuePacket(new PacketPing()), $"Packet {i} should fit.");

        Assert.IsFalse(connection.TryQueuePacket(new PacketPing()), "The queue must apply backpressure at its configured capacity.");
    }

    [TestMethod]
    public void FullGeneralQueueDoesNotConsumeReservedPlayerCapacity()
    {
        var connection = CreateConnection();
        for (int i = 0; i < MiaoClientConnection.PacketChannelSize; i++)
            Assert.IsTrue(connection.TryQueuePacket(new PacketPing()));

        PacketPlayerFrame frame = new(1, 1, new PlayerStateDelta(
            Vector2.Zero,
            string.Empty,
            0,
            Vector2.One,
            PlayerStateDelta.FrameFlags.None,
            PlayerStateFlags.None
        ));

        Assert.IsTrue(connection.TryQueuePacket(frame));
        Assert.IsTrue(connection.TryQueuePacket(new PacketWatchStop(1)));
    }

    [TestMethod]
    public void FullEntityQueueNeverWaitsForCapacity()
    {
        var connection = CreateConnection();
        PacketWatchSceneDelta packet = CreateEntityPacket();
        for (int i = 0; i < MiaoClientConnection.WatchEntityPacketChannelSize; i++)
            Assert.IsTrue(connection.TryQueuePacket(packet));

        ValueTask overflow = connection.QueuePacketAsync(packet);

        Assert.IsTrue(overflow.IsCompletedSuccessfully);
    }

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
        Assert.IsNull(connection.OnResponse(new PacketPong { RequestID = request.RequestID }));
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

        var handler = connection.OnResponse(new PacketPong { RequestID = request.RequestID });
        Assert.IsNotNull(handler);
        await handler(new PacketPong { RequestID = request.RequestID });
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

    private static PacketWatchSceneDelta CreateEntityPacket()
        => new(new WatchSceneDelta(
            1,
            new PlayerLocation("Celeste/1-ForsakenCity", AreaMode.Normal, "1"),
            [],
            [],
            false
        ));

    private sealed class FakeNetworkConnection : INetworkConnection
    {
        public string RemoteAddress => "test";
        public Stream Stream { get; } = new MemoryStream();
        public void Shutdown() { }
        public void Dispose() => Stream.Dispose();
    }
}
