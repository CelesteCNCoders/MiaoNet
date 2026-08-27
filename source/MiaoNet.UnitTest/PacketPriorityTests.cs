using System.Reflection;
using System.Threading.Channels;
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
        Assert.AreEqual(
            PacketPriority.WatchScene,
            PacketPriorityClassifier.Classify(CreateStartResponseTransfer(2))
        );
        Assert.AreEqual(
            PacketPriority.WatchScene,
            PacketPriorityClassifier.Classify(new PacketWatchSceneChunk(17, 0, [1]))
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
    public async Task BoundedChannelWaiterResumesOnlyAfterSpaceIsFreed()
    {
        PriorityPacketChannel queue = new(1, 1, 1, 1);
        PacketPing first = new();
        PacketPing second = new();

        Assert.IsTrue(queue.TryWrite(first));
        Task<bool> pending = queue.WriteAsync(second, CancellationToken.None).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        AssertReadSame(queue, first);
        Assert.IsFalse(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        AssertReadSame(queue, second);
    }

    [TestMethod]
    public async Task BoundedChannelDoesNotLoseWakeupsWithMultipleWriters()
    {
        PriorityPacketChannel queue = new(1, 1, 1, 1);
        PacketPing first = new();
        Assert.IsTrue(queue.TryWrite(first));

        Task<bool>[] pending = Enumerable.Range(0, 4)
            .Select(_ => queue.WriteAsync(new PacketPing(), CancellationToken.None).AsTask())
            .ToArray();
        Assert.IsTrue(pending.All(task => !task.IsCompleted));

        for (int completed = 0; completed < pending.Length; completed++)
        {
            Task<bool>[] remaining = pending.Where(task => !task.IsCompleted).ToArray();
            Assert.IsTrue(queue.TryRead(out _));
            Task<bool> winner = await Task.WhenAny(remaining).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(await winner);
            Assert.AreEqual(completed + 1, pending.Count(task => task.IsCompleted));
        }

        Assert.IsTrue(queue.TryRead(out _));
        Assert.IsFalse(queue.TryRead(out _));
    }

    [TestMethod]
    public async Task CancellingOneBoundedChannelWaiterDoesNotCancelOthers()
    {
        PriorityPacketChannel queue = new(1, 1, 1, 1);
        PacketPing first = new();
        PacketPing livePacket = new();
        Assert.IsTrue(queue.TryWrite(first));

        using CancellationTokenSource cancellation = new();
        Task<bool> cancelled = queue.WriteAsync(new PacketPing(), cancellation.Token).AsTask();
        Task<bool> live = queue.WriteAsync(livePacket, CancellationToken.None).AsTask();
        Assert.IsFalse(cancelled.IsCompleted);
        Assert.IsFalse(live.IsCompleted);

        cancellation.Cancel();
        try
        {
            await cancelled;
            Assert.Fail("The cancelled queue writer unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.IsFalse(live.IsCompleted);
        AssertReadSame(queue, first);
        Assert.IsFalse(await live.WaitAsync(TimeSpan.FromSeconds(2)));
        AssertReadSame(queue, livePacket);
    }

    [TestMethod]
    public async Task CompletingBoundedChannelReleasesBlockedWriters()
    {
        PriorityPacketChannel queue = new(1, 1, 1, 1);
        Assert.IsTrue(queue.TryWrite(new PacketPing()));
        Task<bool> pending = queue.WriteAsync(new PacketPing(), CancellationToken.None).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        queue.Complete();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Fail("The blocked queue writer unexpectedly completed.");
        }
        catch (ChannelClosedException)
        {
        }
    }

    [TestMethod]
    public void NonSaturatedTrafficDoesNotRetainSpaceSignals()
    {
        PriorityPacketChannel queue = new(1, 1, 1, 1);
        for (int i = 0; i < 1000; i++)
        {
            Assert.IsTrue(queue.TryWrite(new PacketPing()));
            Assert.IsTrue(queue.TryRead(out _));
        }

        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo? lanesField = typeof(PriorityPacketChannel).GetField("lanes", PrivateInstance);
        Assert.IsNotNull(lanesField);
        Array lanes = (Array)lanesField.GetValue(queue)!;
        object generalLane = lanes.GetValue((int)PacketPriority.General)!;
        FieldInfo? waitersField = generalLane.GetType().GetField(
            "spaceAvailableWaiters",
            PrivateInstance
        );
        Assert.IsNotNull(waitersField);
        Assert.IsNull(waitersField.GetValue(generalLane));
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

    [TestMethod]
    public void DrainAllBypassesSceneDependenciesAndResetsTimeline()
    {
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new();
        PacketPlayerFrame frame = CreatePlayerFrame(100);
        PacketWatchSceneDelta blockedScene = CreateEntityPacket(101);
        PacketDisconnected disconnected = new(DisconnectReason.PlayerRequested);

        queue.Enqueue(PacketPriority.PlayerTimeline, frame);
        AssertDequeueSame(queue, frame);
        Assert.AreEqual(1, queue.TimelineCount);
        queue.Enqueue(PacketPriority.WatchScene, blockedScene);
        queue.Enqueue(PacketPriority.ConnectionControl, disconnected);

        IReadOnlyList<IContextualPacket> drained = queue.DrainAll();

        CollectionAssert.Contains(drained.ToArray(), blockedScene);
        CollectionAssert.Contains(drained.ToArray(), disconnected);
        Assert.AreEqual(0, queue.Count);
        Assert.AreEqual(0, queue.TimelineCount);

        PacketWatchSceneDelta nextGenerationScene = CreateEntityPacket(1);
        queue.Enqueue(PacketPriority.WatchScene, nextGenerationScene);
        Assert.IsFalse(queue.TryDequeue(out _));
    }

    [TestMethod]
    public void ForgetPlayerPurgesQueuedCausalPacketsAndRejectsLateFrames()
    {
        const int PlayerID = 7;
        HashSet<int> activePlayers = [PlayerID];
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new(activePlayers.Contains);
        PacketContextualPlayerNotification<PacketPlayerFrame> firstFrame = new(
            PlayerID,
            CreatePlayerFrame(1)
        );

        queue.Enqueue(PacketPriority.PlayerTimeline, firstFrame);
        AssertDequeueSame(queue, firstFrame);
        Assert.AreEqual(1, queue.TimelineCount);

        PacketContextualPlayerNotification<PacketPlayerFrame> queuedFrame = new(
            PlayerID,
            CreatePlayerFrame(2)
        );
        PacketWatchSceneDeltaNotification queuedScene = new(
            1,
            PlayerID,
            CreateDelta(2)
        );
        queue.Enqueue(PacketPriority.PlayerTimeline, queuedFrame);
        queue.Enqueue(PacketPriority.WatchScene, queuedScene);

        activePlayers.Remove(PlayerID);
        Assert.AreEqual(2, queue.ForgetPlayer(PlayerID));
        Assert.AreEqual(0, queue.Count);
        Assert.AreEqual(0, queue.TimelineCount);

        PacketContextualPlayerNotification<PacketPlayerFrame> lateFrame = new(
            PlayerID,
            CreatePlayerFrame(3)
        );
        queue.Enqueue(PacketPriority.PlayerTimeline, lateFrame);
        Assert.IsFalse(queue.TryDequeue(out _));
        Assert.AreEqual(0, queue.Count);
        Assert.AreEqual(0, queue.TimelineCount);
    }

    [TestMethod]
    public void RemotePlayerClassifierCoversOrdinaryEventsButPreservesLifecyclePackets()
    {
        const int PlayerID = 7;
        IContextualPacket[] packets =
        [
            new PacketEmoteText(PlayerID, "late"),
            new PacketPlayerNotification<PacketUpdateGlobalFlag>(
                PlayerID,
                new(PlayerGlobalFlags.None)
            ),
            new PacketPlayerNotification<PacketCreateFireworks>(
                PlayerID,
                new(Color.White, 1f)
            ),
            new PacketContextualPlayerNotification<PacketPlayerPlayedAudio>(
                PlayerID,
                new(new PlayerPlayedAudio("event:/char/madeline/dash_red_right"))
            ),
            new PacketPlayerGrabPlayer(PlayerID),
            new PacketPlayerGrabJumpOut(PlayerID),
            new PacketChatMessage(DateTime.UnixEpoch, ChatMessageType.Chat, PlayerID, "late"),
        ];

        foreach (IContextualPacket packet in packets)
        {
            Assert.AreEqual(
                InactivePlayerPacketDisposition.Discard,
                RemotePlayerPacket.GetInactiveDisposition(packet, out int playerKey)
            );
            Assert.AreEqual(PlayerID, playerKey);
        }

        Assert.AreEqual(
            InactivePlayerPacketDisposition.Unrelated,
            RemotePlayerPacket.GetInactiveDisposition(new PacketPlayerLeft(PlayerID), out _)
        );
        Assert.AreEqual(
            InactivePlayerPacketDisposition.Unrelated,
            RemotePlayerPacket.GetInactiveDisposition(
                new PacketChatMessage(DateTime.UnixEpoch, ChatMessageType.Server, null, "notice"),
                out _
            )
        );
        Assert.AreEqual(
            InactivePlayerPacketDisposition.Deliver,
            RemotePlayerPacket.GetInactiveDisposition(
                CreateStartResponse(PlayerID, 2),
                out int responsePlayerKey
            )
        );
        Assert.AreEqual(PlayerID, responsePlayerKey);
        Assert.AreEqual(
            InactivePlayerPacketDisposition.Deliver,
            RemotePlayerPacket.GetInactiveDisposition(
                CreateStartResponseTransfer(PlayerID),
                out int transferPlayerKey
            )
        );
        Assert.AreEqual(PlayerID, transferPlayerKey);
        Assert.AreEqual(
            InactivePlayerPacketDisposition.Unrelated,
            RemotePlayerPacket.GetInactiveDisposition(
                new PacketWatchSceneChunk(17, 0, [1]),
                out _
            )
        );
    }

    [TestMethod]
    public void RequiredWatchStartResponseWaitsWhileActiveAndSurvivesDeparture()
    {
        const int PlayerID = 7;
        HashSet<int> activePlayers = [PlayerID];
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new(activePlayers.Contains);
        PacketWatchStartResponse response = CreateStartResponse(PlayerID, 2);

        queue.Enqueue(PacketPriority.ConnectionControl, response);
        Assert.IsFalse(queue.TryDequeue(out _));

        activePlayers.Remove(PlayerID);
        Assert.AreEqual(0, queue.ForgetPlayer(PlayerID));
        AssertDequeueSame(queue, response);
        Assert.IsTrue(queue.IsEmpty);
    }

    [TestMethod]
    public void RequiredWatchStartResponseStillHonorsActiveTimelineDependency()
    {
        const int PlayerID = 7;
        HashSet<int> activePlayers = [PlayerID];
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new(activePlayers.Contains);
        PacketWatchStartResponse response = CreateStartResponse(PlayerID, 2);
        PacketContextualPlayerNotification<PacketPlayerFrame> frame = new(
            PlayerID,
            CreatePlayerFrame(2)
        );

        queue.Enqueue(PacketPriority.ConnectionControl, response);
        queue.Enqueue(PacketPriority.PlayerTimeline, frame);

        AssertDequeueSame(queue, frame);
        AssertDequeueSame(queue, response);
    }

    [TestMethod]
    public void ForgetPlayerPurgesOrdinaryEventsWithoutAffectingOtherPlayers()
    {
        const int DepartedPlayerID = 7;
        const int ActivePlayerID = 8;
        HashSet<int> activePlayers = [DepartedPlayerID, ActivePlayerID];
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new(activePlayers.Contains);
        IContextualPacket[] departedPackets =
        [
            new PacketEmoteText(DepartedPlayerID, "late"),
            new PacketPlayerNotification<PacketUpdateGlobalFlag>(
                DepartedPlayerID,
                new(PlayerGlobalFlags.None)
            ),
            new PacketPlayerGrabJumpOut(DepartedPlayerID),
        ];
        PacketEmoteText activePacket = new(ActivePlayerID, "active");

        foreach (IContextualPacket packet in departedPackets)
            queue.Enqueue(PacketPriorityClassifier.Classify(packet), packet);
        queue.Enqueue(PacketPriorityClassifier.Classify(activePacket), activePacket);

        activePlayers.Remove(DepartedPlayerID);
        Assert.AreEqual(departedPackets.Length, queue.ForgetPlayer(DepartedPlayerID));
        Assert.AreEqual(1, queue.Count);
        AssertDequeueSame(queue, activePacket);

        PacketChatMessage latePacket = new(
            DateTime.UnixEpoch,
            ChatMessageType.Chat,
            DepartedPlayerID,
            "late"
        );
        queue.Enqueue(PacketPriorityClassifier.Classify(latePacket), latePacket);
        Assert.IsFalse(queue.TryDequeue(out _));
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void InactiveSnapshotBaselineDoesNotRestoreTimeline()
    {
        const int PlayerID = 7;
        HashSet<int> activePlayers = [PlayerID];
        ConcurrentPacketPriorityQueue<IContextualPacket> queue = new(activePlayers.Contains);
        PacketPlayerLocationChangedResponse response = new(
            [new PlayerMovedInitialDataWithID(
                PlayerID,
                new PlayerMovedInitialData(4, 20, CreateState(Vector2.One, 1))
            )]
        );

        activePlayers.Remove(PlayerID);
        queue.Enqueue(PacketPriority.PlayerTimeline, response);

        AssertDequeueSame(queue, response);
        Assert.AreEqual(0, queue.TimelineCount);
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

    private static PacketWatchSceneDelta CreateEntityPacket(uint playerSequenceWatermark = 1)
        => new(CreateDelta(playerSequenceWatermark));

    private static PacketWatchStartResponse CreateStartResponse(
        int targetPlayerID,
        uint playerSequenceWatermark
    ) => new(
        WatchStartResult.Success,
        9,
        new WatchSceneSnapshot(
            Location,
            1,
            [],
            [],
            playerEpoch: 1,
            playerSequenceWatermark: playerSequenceWatermark
        ),
        targetPlayerID
    ) { RequestID = 10 };

    private static PacketWatchSceneTransferStart CreateStartResponseTransfer(int targetPlayerID)
        => new(new WatchSceneTransferDescriptor(
            17,
            WatchSceneTransferKind.StartResponse,
            WatchSceneFragmenter.FragmentSize + 1,
            2,
            1,
            1,
            2,
            10,
            9,
            targetPlayerID
        ));

    private static WatchSceneDelta CreateDelta(uint playerSequenceWatermark = 1)
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
            playerSequenceWatermark: playerSequenceWatermark
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
