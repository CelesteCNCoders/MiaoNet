using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PacketPriorityTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/1-ForsakenCity",
        AreaMode.Normal,
        "1"
    );

    [TestMethod]
    public void ClassifierSeparatesControlPlayerGeneralAndEntityPackets()
    {
        Assert.AreEqual(
            PacketPriority.ConnectionControl,
            PacketPriorityClassifier.Classify(new PacketWatchStop(1))
        );
        Assert.AreEqual(
            PacketPriority.PlayerTimeline,
            PacketPriorityClassifier.Classify(CreatePlayerFrame())
        );
        Assert.AreEqual(
            PacketPriority.PlayerTimeline,
            PacketPriorityClassifier.Classify(
                new PacketContextualPlayerNotification<PacketPlayerFrame>(1, CreatePlayerFrame())
            )
        );
        Assert.AreEqual(
            PacketPriority.General,
            PacketPriorityClassifier.Classify(new PacketPing())
        );
        Assert.AreEqual(
            PacketPriority.WatchScene,
            PacketPriorityClassifier.Classify(CreateEntityPacket())
        );
        Assert.AreEqual(
            PacketPriority.WatchScene,
            PacketPriorityClassifier.Classify(
                new PacketWatchSceneDeltaNotification(1, 2, CreateDelta())
            )
        );
    }

    [TestMethod]
    public void ConcurrentQueueDequeuesByPriorityAndKeepsLaneOrder()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PacketWatchSceneDelta entity1 = CreateEntityPacket();
        PacketWatchSceneDelta entity2 = CreateEntityPacket();
        PacketPing general = new();
        PacketPlayerFrame player1 = CreatePlayerFrame(1);
        PacketPlayerFrame player2 = CreatePlayerFrame(2);
        PacketWatchStop control = new(1);

        queue.Enqueue(PacketPriority.WatchScene, entity1);
        queue.Enqueue(PacketPriority.PlayerTimeline, player1);
        queue.Enqueue(PacketPriority.General, general);
        queue.Enqueue(PacketPriority.WatchScene, entity2);
        queue.Enqueue(PacketPriority.PlayerTimeline, player2);
        queue.Enqueue(PacketPriority.ConnectionControl, control);

        AssertDequeueSame(queue, control);
        AssertDequeueSame(queue, player1);
        AssertDequeueSame(queue, general);
        AssertDequeueSame(queue, entity1);
        AssertDequeueSame(queue, player2);
        AssertDequeueSame(queue, entity2);
        Assert.IsTrue(queue.IsEmpty);
    }

    [TestMethod]
    public void NonEntityDrainLeavesEntityPacketsForASeparateRebuildLane()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PacketWatchSceneDelta entity = CreateEntityPacket();
        PacketPlayerFrame player = CreatePlayerFrame();
        queue.Enqueue(PacketPriority.WatchScene, entity);

        Assert.IsFalse(queue.TryDequeueNonEntity(out _));
        queue.Enqueue(PacketPriority.PlayerTimeline, player);
        Assert.IsTrue(queue.TryDequeueNonEntity(out IContextualPacket actual));
        Assert.AreSame(player, actual);
        AssertDequeueSame(queue, entity);
    }

    [TestMethod]
    public void BoundedChannelReservesCapacityPerPriorityLane()
    {
        PriorityPacketChannel queue = new(1, 1, 1, 1);
        PacketPing general = new();
        PacketWatchSceneDelta entity = CreateEntityPacket();
        PacketPlayerFrame player = CreatePlayerFrame();
        PacketWatchStop control = new(1);

        Assert.IsTrue(queue.TryWrite(general));
        Assert.IsFalse(queue.TryWrite(new PacketPing()));
        Assert.IsTrue(queue.TryWrite(entity));
        Assert.IsFalse(queue.TryWrite(CreateEntityPacket()));
        Assert.IsTrue(queue.TryWrite(player));
        Assert.IsTrue(queue.TryWrite(control));

        AssertReadSame(queue, control);
        AssertReadSame(queue, player);
        AssertReadSame(queue, general);
        AssertReadSame(queue, entity);
        Assert.IsFalse(queue.TryRead(out _));
    }

    [TestMethod]
    public void SameEpochTailFrameIsPromotedToLatestKeyframe()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PlayerState firstState = CreateState(new Vector2(1f, 2f), 1);
        PlayerState latestState = CreateState(new Vector2(7f, 8f), 2);
        PacketPlayerFrame first = CreateCoalescibleFrame(4, 10, firstState);
        PacketPlayerFrame latest = CreateCoalescibleFrame(4, 11, latestState);

        Assert.IsTrue(queue.Enqueue(PacketPriority.PlayerTimeline, first));
        Assert.IsFalse(queue.Enqueue(PacketPriority.PlayerTimeline, latest));
        Assert.AreEqual(1, queue.Count);
        Assert.IsTrue(queue.TryDequeue(out IContextualPacket dequeued));

        PacketPlayerFrame keyframe = (PacketPlayerFrame)dequeued;
        Assert.AreEqual(PlayerFrameKind.Keyframe, keyframe.Kind);
        Assert.AreEqual((uint)4, keyframe.PlayerEpoch);
        Assert.AreEqual((uint)11, keyframe.PlayerSequence);
        Assert.AreEqual(latestState.Position, keyframe.KeyframeState!.Position);
        Assert.AreEqual(latestState.Dashes, keyframe.KeyframeState.Dashes);
    }

    [TestMethod]
    public void TimelineBarrierPreventsCrossEpochFrameReplacement()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PlayerState firstState = CreateState(Vector2.Zero, 1);
        PlayerState secondState = CreateState(Vector2.One, 2);
        PacketPlayerFrame oldFrame = CreateCoalescibleFrame(7, 3, firstState);
        PacketPlayerLocationChanged barrier = new(8, 0, Location, secondState);
        PacketPlayerFrame newFrame = CreateCoalescibleFrame(8, 1, secondState);

        queue.Enqueue(PacketPriority.PlayerTimeline, oldFrame);
        queue.Enqueue(PacketPriority.PlayerTimeline, barrier);
        queue.Enqueue(PacketPriority.PlayerTimeline, newFrame);

        AssertDequeueSame(queue, oldFrame);
        AssertDequeueSame(queue, barrier);
        AssertDequeueSame(queue, newFrame);
    }

    [TestMethod]
    public void SceneWatermarkWaitsForPlayerTimeline()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PacketWatchSceneDelta scene = CreateEntityPacket();
        PacketPlayerFrame frame = CreatePlayerFrame();

        queue.Enqueue(PacketPriority.WatchScene, scene);
        Assert.IsFalse(queue.TryDequeue(out _));

        queue.Enqueue(PacketPriority.PlayerTimeline, frame);
        AssertDequeueSame(queue, frame);
        AssertDequeueSame(queue, scene);
    }

    private static void AssertDequeueSame(
        ConcurrentPacketPriorityQueue<IContextualPacket> queue,
        IContextualPacket expected
    )
    {
        Assert.IsTrue(queue.TryDequeue(out IContextualPacket actual));
        Assert.AreSame(expected, actual);
    }

    private static void AssertReadSame(PriorityPacketChannel queue, IContextualPacket expected)
    {
        Assert.IsTrue(queue.TryRead(out IContextualPacket actual));
        Assert.AreSame(expected, actual);
    }

    private static PacketPlayerFrame CreatePlayerFrame(uint sequence = 1)
        => new(1, sequence, new PlayerStateDelta(
            Vector2.Zero,
            string.Empty,
            0,
            Vector2.One,
            PlayerStateDelta.FrameFlags.None,
            PlayerStateFlags.None
        ));

    private static PacketWatchSceneDelta CreateEntityPacket()
        => new(CreateDelta());

    private static WatchSceneDelta CreateDelta()
        => new(
            1,
            Location,
            [],
            [],
            false,
            WatchEntityStateMode.None,
            [],
            [],
            playerEpoch: 1,
            playerSequenceWatermark: 1
        );

    private static PacketPlayerFrame CreateCoalescibleFrame(
        uint epoch,
        uint sequence,
        PlayerState state
    )
        => new(
            epoch,
            sequence,
            new PlayerStateDelta(
                state.Position,
                state.Animation,
                state.AnimationFrame,
                state.Scale,
                PlayerStateDelta.FrameFlags.DashesChange,
                state.StateFlags
            ) { Dashes = state.Dashes },
            state
        );

    private static PlayerState CreateState(Vector2 position, byte dashes)
        => new()
        {
            Position = position,
            Animation = "idle",
            AnimationFrame = 0,
            Scale = Vector2.One,
            StateFlags = PlayerStateFlags.None,
            Dashes = dashes,
            DeltaTime = 0f,
            PlayerSpriteMode = PlayerSpriteMode.Madeline,
            HoldableInfo = new(),
            FollowerInfos = [],
            WindDirection = Vector2.Zero,
        };
}
