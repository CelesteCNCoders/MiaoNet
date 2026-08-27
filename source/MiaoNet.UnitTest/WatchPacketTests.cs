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
        WatchSceneSnapshot snapshot = new(
            Location,
            3,
            ["flag-a", "flag-b"],
            [
                new(new(WatchEntityKind.TouchSwitchAndSwitchGate, 0, 1),
                    [.. BitConverter.GetBytes(2), .. BitConverter.GetBytes(9)]),
                new(new(WatchEntityKind.Spring, 12), [1, 2]),
            ]
        );
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
        WatchSceneSnapshot snapshot = new(Location, 0, ["flag"]);
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
        WatchEntityKey key = new(WatchEntityKind.Spring, 12, 2);
        WatchSceneDelta delta = new(
            8,
            Location,
            ["added"],
            ["removed"],
            true,
            WatchEntityStateMode.Replace,
            [new(key, [4, 5])],
            [new(key, 1, [6, 7])]
        );
        WatchRoomTransition roomTransition = new(
            new PlayerLocation(Location.Map, "0"),
            Location,
            new Vector2(320f, 180f),
            new Vector2(0f, 1f)
        );
        WatchSceneDelta transitionDelta = new(
            9,
            Location,
            [],
            [],
            false,
            WatchEntityStateMode.Replace,
            [],
            [],
            false,
            roomTransition
        );

        PacketWatchSceneDelta readDelta = await RoundTripAsync(new PacketWatchSceneDelta(delta));
        PacketWatchSceneDelta readTransitionDelta = await RoundTripAsync(
            new PacketWatchSceneDelta(transitionDelta)
        );
        PacketWatchSceneDeltaNotification readNotification = await RoundTripAsync(
            new PacketWatchSceneDeltaNotification(1, 2, delta)
        );
        PacketWatchResyncRequest readResyncRequest = await RoundTripAsync(
            new PacketWatchResyncRequest(1, 7)
        );
        PacketWatchResyncSnapshot readResyncSnapshot = await RoundTripAsync(
            new PacketWatchResyncSnapshot(
                1,
                2,
                new WatchSceneSnapshot(Location, 8, ["resynced"])
            )
        );
        PacketWatchStop readStop = await RoundTripAsync(new PacketWatchStop(1));
        PacketWatchProducerStop readProducerStop = await RoundTripAsync(new PacketWatchProducerStop(1));
        PacketWatchEnded readEnded = await RoundTripAsync(
            new PacketWatchEnded(1, WatchEndReason.LocationChanged)
        );

        AssertDelta(delta, readDelta.Delta);
        AssertDelta(transitionDelta, readTransitionDelta.Delta);
        Assert.AreEqual(1, readNotification.SessionID);
        Assert.AreEqual(2, readNotification.TargetPlayerID);
        AssertDelta(delta, readNotification.Delta);
        Assert.AreEqual(1, readResyncRequest.SessionID);
        Assert.AreEqual(7, readResyncRequest.LastAppliedSequence);
        Assert.AreEqual(1, readResyncSnapshot.SessionID);
        Assert.AreEqual(2, readResyncSnapshot.TargetPlayerID);
        Assert.AreEqual(8, readResyncSnapshot.Snapshot.Sequence);
        CollectionAssert.AreEqual(
            new[] { "resynced" },
            readResyncSnapshot.Snapshot.Flags.ToArray()
        );
        Assert.AreEqual(1, readStop.SessionID);
        Assert.AreEqual(1, readProducerStop.SessionID);
        Assert.AreEqual(1, readEnded.SessionID);
        Assert.AreEqual(WatchEndReason.LocationChanged, readEnded.Reason);
    }

    [TestMethod]
    public async Task DeathWipeNotificationRoundTripsWithoutPositionPayloadMeaning()
    {
        PacketPlayerLiveState packet = new(3, 9, LiveStateType.DeathWipe, Vector2.Zero);

        PacketPlayerLiveState read = await RoundTripAsync(packet);

        Assert.AreEqual((uint)3, read.PlayerEpoch);
        Assert.AreEqual((uint)9, read.PlayerSequence);
        Assert.AreEqual(LiveStateType.DeathWipe, read.Type);
        Assert.AreEqual(Vector2.Zero, read.Vector2);
    }

    [TestMethod]
    public async Task WatchedCameraPositionRoundTripsInFrameState()
    {
        Vector2 frameCamera = new(130f, 460f);
        PlayerStateDelta delta = new(
            new Vector2(11f, 21f),
            "runFast",
            1,
            Vector2.One,
            PlayerStateDelta.FrameFlags.HasCameraPosition,
            PlayerStateFlags.None
        )
        {
            CameraPosition = frameCamera,
        };

        PacketPlayerFrame readFrame = await RoundTripAsync(new PacketPlayerFrame(3, 10, delta));

        Assert.AreEqual((uint)3, readFrame.PlayerEpoch);
        Assert.AreEqual((uint)10, readFrame.PlayerSequence);
        Assert.IsTrue(readFrame.StateDelta!.HasCameraPosition);
        Assert.AreEqual(frameCamera, readFrame.StateDelta.CameraPosition);
    }

    [TestMethod]
    public async Task PlayerKeyframeRoundTripsCompleteRecoveryState()
    {
        PlayerState state = new()
        {
            Position = new Vector2(4f, 5f),
            Animation = "dash",
            AnimationFrame = 2,
            Scale = Vector2.One,
            StateFlags = PlayerStateFlags.Dashing,
            Dashes = 2,
            LastDashDirection = 1.25f,
            DeltaTime = 1f / 60f,
            PlayerSpriteMode = PlayerSpriteMode.Madeline,
            HoldableInfo = new(HoldableType.Theo, new Vector2(1f, 2f)),
            FollowerInfos = [new(FollowerType.Key, "key", "idle", 0, new Vector2S(2, 3))],
            WindDirection = new Vector2(1f, 0f),
        };

        PacketPlayerFrame read = await RoundTripAsync(new PacketPlayerFrame(
            8,
            17,
            state,
            new Vector2(100f, 200f)
        ));

        Assert.AreEqual(PlayerFrameKind.Keyframe, read.Kind);
        Assert.AreEqual((uint)8, read.PlayerEpoch);
        Assert.AreEqual((uint)17, read.PlayerSequence);
        Assert.AreEqual(state.Position, read.KeyframeState!.Position);
        Assert.AreEqual(state.Dashes, read.KeyframeState.Dashes);
        Assert.HasCount(1, read.KeyframeState.FollowerInfos);
        Assert.IsTrue(read.HasCameraPosition);
        Assert.AreEqual(new Vector2(100f, 200f), read.CameraPosition);
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
        AssertEntityStates(expected.EntityStates, actual.EntityStates);
    }

    private static void AssertDelta(WatchSceneDelta expected, WatchSceneDelta actual)
    {
        Assert.AreEqual(expected.Sequence, actual.Sequence);
        Assert.AreEqual(expected.Location, actual.Location);
        CollectionAssert.AreEqual(expected.AddedFlags.ToArray(), actual.AddedFlags.ToArray());
        CollectionAssert.AreEqual(expected.RemovedFlags.ToArray(), actual.RemovedFlags.ToArray());
        Assert.AreEqual(expected.RequiresRoomReload, actual.RequiresRoomReload);
        Assert.AreEqual(expected.IsDeathRespawn, actual.IsDeathRespawn);
        Assert.AreEqual(expected.RoomTransition, actual.RoomTransition);
        Assert.AreEqual(expected.EntityStateMode, actual.EntityStateMode);
        AssertEntityStates(expected.EntityStates, actual.EntityStates);
        WatchEntityEvent[] expectedEvents = expected.EntityEvents.ToArray();
        WatchEntityEvent[] actualEvents = actual.EntityEvents.ToArray();
        Assert.HasCount(expectedEvents.Length, actualEvents);
        for (int i = 0; i < expectedEvents.Length; i++)
        {
            Assert.AreEqual(expectedEvents[i].Key, actualEvents[i].Key);
            Assert.AreEqual(expectedEvents[i].EventID, actualEvents[i].EventID);
            CollectionAssert.AreEqual(
                expectedEvents[i].Payload.ToArray(),
                actualEvents[i].Payload.ToArray()
            );
        }
    }

    private static void AssertEntityStates(
        IReadOnlyCollection<WatchEntityState> expected,
        IReadOnlyCollection<WatchEntityState> actual
    )
    {
        WatchEntityState[] expectedStates = expected.ToArray();
        WatchEntityState[] actualStates = actual.ToArray();
        Assert.HasCount(expectedStates.Length, actualStates);
        for (int i = 0; i < expectedStates.Length; i++)
        {
            Assert.AreEqual(expectedStates[i].Key, actualStates[i].Key);
            CollectionAssert.AreEqual(
                expectedStates[i].Payload.ToArray(),
                actualStates[i].Payload.ToArray()
            );
        }
    }

    private sealed class TestPacketSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
