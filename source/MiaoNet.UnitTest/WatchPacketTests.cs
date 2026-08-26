using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchPacketTests
{
    private readonly TestPacketSerializationContext context = new();

    private static readonly PlayerLocation Location = new(
        "Celeste/1-ForsakenCity",
        AreaMode.Normal,
        "1"
    );

    [TestMethod]
    public async Task RequestAndSuccessfulResponseRoundTrip()
    {
        PacketWatchStart request = new(42) { RequestID = 7 };
        WatchSceneSnapshot snapshot = new(Location, 3, ["flag-a", "flag-b"], [2, 9]);
        PacketWatchStartResponse response = new(WatchStartResult.Success, 9, snapshot)
        {
            RequestID = 7,
        };

        PacketWatchStart readRequest = await RoundTripAsync(request);
        PacketWatchStartResponse readResponse = await RoundTripAsync(response);

        Assert.AreEqual(7, readRequest.RequestID);
        Assert.AreEqual(42, readRequest.TargetPlayerID);
        Assert.AreEqual(7, readResponse.RequestID);
        Assert.IsTrue(readResponse.IsSuccess);
        Assert.AreEqual(9, readResponse.SessionID);
        AssertSnapshot(snapshot, readResponse.Snapshot);
    }

    [TestMethod]
    public async Task SnapshotExchangeRoundTripsSuccessAndFailure()
    {
        PacketWatchSnapshotRequest request = new(4, Location) { RequestID = 5 };
        WatchSceneSnapshot snapshot = new(Location, 0, ["flag"], [4]);
        PacketWatchSnapshotResponse success = new(WatchSnapshotResult.Success, snapshot)
        {
            RequestID = 5,
        };
        PacketWatchSnapshotResponse failure = new(WatchSnapshotResult.LocationChanged, null)
        {
            RequestID = 6,
        };

        PacketWatchSnapshotRequest readRequest = await RoundTripAsync(request);
        PacketWatchSnapshotResponse readSuccess = await RoundTripAsync(success);
        PacketWatchSnapshotResponse readFailure = await RoundTripAsync(failure);

        Assert.AreEqual(5, readRequest.RequestID);
        Assert.AreEqual(4, readRequest.SessionID);
        Assert.IsTrue(Location == readRequest.ExpectedLocation);
        Assert.IsTrue(readSuccess.IsSuccess);
        AssertSnapshot(snapshot, readSuccess.Snapshot);
        Assert.AreEqual(WatchSnapshotResult.LocationChanged, readFailure.Result);
        Assert.IsNull(readFailure.Snapshot);
    }

    [TestMethod]
    public async Task DeltaAndLifecyclePacketsRoundTrip()
    {
        WatchSceneDelta delta = new(8, Location, ["added"], ["removed"], true, true, [3, 7]);

        PacketWatchSceneDelta readDelta = await RoundTripAsync(new PacketWatchSceneDelta(delta));
        PacketWatchSceneDeltaNotification readNotification = await RoundTripAsync(
            new PacketWatchSceneDeltaNotification(1, 2, delta)
        );
        PacketWatchStop readStop = await RoundTripAsync(new PacketWatchStop(1));
        PacketWatchProducerStop readProducerStop = await RoundTripAsync(new PacketWatchProducerStop(1));
        PacketWatchEnded readEnded = await RoundTripAsync(
            new PacketWatchEnded(1, WatchEndReason.LocationChanged)
        );

        AssertDelta(delta, readDelta.Delta);
        Assert.AreEqual(1, readNotification.SessionID);
        Assert.AreEqual(2, readNotification.TargetPlayerID);
        AssertDelta(delta, readNotification.Delta);
        Assert.AreEqual(1, readStop.SessionID);
        Assert.AreEqual(1, readProducerStop.SessionID);
        Assert.AreEqual(1, readEnded.SessionID);
        Assert.AreEqual(WatchEndReason.LocationChanged, readEnded.Reason);
    }

    private async Task<TPacket> RoundTripAsync<TPacket>(TPacket packet)
        where TPacket : class, IContextualPacket
    {
        using MemoryStream stream = new();
        PacketFraming.WritePacket(stream, packet, context);
        stream.Position = 0;

        IContextualPacket? result = await PacketFraming.ReadPacketAsync(
            stream,
            context,
            CancellationToken.None
        );

        return Assert.IsInstanceOfType<TPacket>(result);
    }

    private static void AssertSnapshot(WatchSceneSnapshot expected, WatchSceneSnapshot actual)
    {
        Assert.AreEqual(expected.Location, actual.Location);
        Assert.AreEqual(expected.Sequence, actual.Sequence);
        CollectionAssert.AreEqual(expected.Flags.ToArray(), actual.Flags.ToArray());
        CollectionAssert.AreEqual(
            expected.ActiveTouchSwitchIDs.ToArray(),
            actual.ActiveTouchSwitchIDs.ToArray()
        );
    }

    private static void AssertDelta(WatchSceneDelta expected, WatchSceneDelta actual)
    {
        Assert.AreEqual(expected.Sequence, actual.Sequence);
        Assert.AreEqual(expected.Location, actual.Location);
        CollectionAssert.AreEqual(expected.AddedFlags.ToArray(), actual.AddedFlags.ToArray());
        CollectionAssert.AreEqual(expected.RemovedFlags.ToArray(), actual.RemovedFlags.ToArray());
        Assert.AreEqual(expected.RequiresRoomReload, actual.RequiresRoomReload);
        Assert.AreEqual(expected.HasTouchSwitchState, actual.HasTouchSwitchState);
        CollectionAssert.AreEqual(
            expected.ActiveTouchSwitchIDs.ToArray(),
            actual.ActiveTouchSwitchIDs.ToArray()
        );
    }

    private sealed class TestPacketSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
