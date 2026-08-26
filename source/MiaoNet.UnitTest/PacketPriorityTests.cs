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
            PacketPriority.Control,
            PacketPriorityClassifier.Classify(new PacketWatchStop(1))
        );
        Assert.AreEqual(
            PacketPriority.PlayerFrame,
            PacketPriorityClassifier.Classify(CreatePlayerFrame())
        );
        Assert.AreEqual(
            PacketPriority.PlayerFrame,
            PacketPriorityClassifier.Classify(
                new PacketContextualPlayerNotification<PacketPlayerFrame>(1, CreatePlayerFrame())
            )
        );
        Assert.AreEqual(
            PacketPriority.General,
            PacketPriorityClassifier.Classify(new PacketPing())
        );
        Assert.AreEqual(
            PacketPriority.WatchEntity,
            PacketPriorityClassifier.Classify(CreateEntityPacket())
        );
        Assert.AreEqual(
            PacketPriority.WatchEntity,
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
        PacketPlayerFrame player1 = CreatePlayerFrame();
        PacketPlayerFrame player2 = CreatePlayerFrame();
        PacketWatchStop control = new(1);

        queue.Enqueue(PacketPriority.WatchEntity, entity1);
        queue.Enqueue(PacketPriority.PlayerFrame, player1);
        queue.Enqueue(PacketPriority.General, general);
        queue.Enqueue(PacketPriority.WatchEntity, entity2);
        queue.Enqueue(PacketPriority.PlayerFrame, player2);
        queue.Enqueue(PacketPriority.Control, control);

        AssertDequeueSame(queue, control);
        AssertDequeueSame(queue, player1);
        AssertDequeueSame(queue, player2);
        AssertDequeueSame(queue, general);
        AssertDequeueSame(queue, entity1);
        AssertDequeueSame(queue, entity2);
        Assert.IsTrue(queue.IsEmpty);
    }

    [TestMethod]
    public void NonEntityDrainLeavesEntityPacketsForASeparateRebuildLane()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PacketWatchSceneDelta entity = CreateEntityPacket();
        PacketPlayerFrame player = CreatePlayerFrame();
        queue.Enqueue(PacketPriority.WatchEntity, entity);

        Assert.IsFalse(queue.TryDequeueNonEntity(out _));
        queue.Enqueue(PacketPriority.PlayerFrame, player);
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

    private static PacketPlayerFrame CreatePlayerFrame()
        => new(new PlayerStateDelta(
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
        => new(1, Location, [], [], false);
}
