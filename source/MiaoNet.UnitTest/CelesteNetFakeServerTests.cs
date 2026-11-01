using MiaoNet.CelesteNetBridge.Protocol;
using MiaoNet.CelesteNetBridge.Server;

namespace MiaoNet.UnitTest;

[TestClass]
public class CelesteNetFakeServerTests {

    private static CelesteNetFakeServer StartServer(Action<CelesteNetFakeSession>? onSession = null) {
        var options = new CelesteNetFakeServerOptions {
            Port = 0,
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
        };
        var server = new CelesteNetFakeServer(options);
        if (onSession != null)
            server.SessionStarted += onSession;
        server.Start();
        return server;
    }

    [TestMethod]
    public async Task ServesHandshakeStartupSequenceAndKeepAlive() {
        using CelesteNetFakeServer server = StartServer();
        using CelesteNetTestClient client = new(server.Port);

        await client.HandshakeAsync();

        byte[] tickRate = await client.ReceiveAsync();
        Assert.AreEqual("tickRate", CelesteNetTypeReader.TryReadTypeId(tickRate));
        CollectionAssert.AreEqual(CelesteNetDataTypes.TickRate(60f), tickRate);

        byte[] channelList = await client.ReceiveAsync();
        Assert.AreEqual("channelList", CelesteNetTypeReader.TryReadTypeId(channelList));
        CollectionAssert.AreEqual(
            CelesteNetDataTypes.ChannelList([("main", 0u, [1u])]),
            channelList
        );

        byte[] playerInfo = await client.ReceiveAsync();
        Assert.AreEqual("playerInfo", CelesteNetTypeReader.TryReadTypeId(playerInfo));
        CollectionAssert.AreEqual(CelesteNetDataTypes.PlayerInfo(1, "Player"), playerInfo);

        byte[] ready = await client.ReceiveAsync();
        Assert.AreEqual("ready", CelesteNetTypeReader.TryReadTypeId(ready));
        CollectionAssert.AreEqual(CelesteNetDataTypes.Ready(), ready);

        byte[] keepAlive = await client.ReceiveAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("keepAlive", CelesteNetTypeReader.TryReadTypeId(keepAlive));
    }

    [TestMethod]
    public async Task RelaysInboundFramesToTheServerSideByteForByte() {
        TaskCompletionSource<CelesteNetFrame> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CelesteNetFakeServer server = StartServer(session => session.FrameReceived += (_, frame) => received.TrySetResult(frame));
        using CelesteNetTestClient client = new(server.Port);

        await client.HandshakeAsync();
        for (int i = 0; i < 4; i++)
            await client.ReceiveAsync();

        byte[] payload = CelesteNetDataTypes.Encode(
            0,
            "corkr900CoopHelper_BundledEntityUpdate_1_1_0",
            [1, 2, 3, 4, 5, 6, 7, 8]
        );
        await client.SendAsync(payload);

        CelesteNetFrame frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("corkr900CoopHelper_BundledEntityUpdate_1_1_0", frame.TypeId);
        CollectionAssert.AreEqual(payload, frame.Payload);
    }

    [TestMethod]
    public async Task ReassemblesFramesSplitAcrossTcpWrites() {
        TaskCompletionSource<CelesteNetFrame> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CelesteNetFakeServer server = StartServer(session => session.FrameReceived += (_, frame) => received.TrySetResult(frame));
        using CelesteNetTestClient client = new(server.Port);

        await client.HandshakeAsync();
        for (int i = 0; i < 4; i++)
            await client.ReceiveAsync();

        byte[] payload = CelesteNetDataTypes.Encode(0, "splitType", [9, 9, 9]);
        byte[] framed = CelesteNetFraming.Wrap(payload);
        foreach (byte b in framed) {
            await client.SendRawAsync([b]);
            await Task.Delay(2);
        }

        CelesteNetFrame frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("splitType", frame.TypeId);
        CollectionAssert.AreEqual(payload, frame.Payload);
    }

    [TestMethod]
    public async Task SessionIsMarkedClosedWhenTheClientDisconnects() {
        TaskCompletionSource<bool> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CelesteNetFakeServer server = StartServer(session => session.Closed += _ => closed.TrySetResult(true));
        using (CelesteNetTestClient client = new(server.Port)) {
            await client.HandshakeAsync();
            for (int i = 0; i < 4; i++)
                await client.ReceiveAsync();
        }

        Assert.IsTrue(await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
