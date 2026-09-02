namespace MiaoNet.Shared;

internal enum PacketPriority : byte
{
    ConnectionControl,
    PlayerTimeline,
    General,
    WatchScene,
}

internal static class PacketPriorityClassifier
{
    internal static PacketPriority Classify(IContextualPacket packet)
        => packet switch
        {
            PacketPlayerFrame
                or PacketContextualPlayerNotification<PacketPlayerFrame>
                or PacketPlayerLiveState
                or PacketPlayerNotification<PacketPlayerLiveState>
                or PacketPlayerLocationChanged
                or PacketPlayerLocationChangedNotification
                or PacketPlayerChannelMove
                or PacketPlayerChannelMovedNotification
                or PacketPlayerLocationChangedResponse
                or PacketPlayerChannelMovedResponse
                or PacketWatchTargetRestarting
                or PacketWatchTargetRestartingNotification
                => PacketPriority.PlayerTimeline,

            PacketWatchSceneDelta
                or PacketWatchSceneDeltaNotification
                or PacketWatchSceneTransferStart
                or PacketWatchSceneChunk
                => PacketPriority.WatchScene,

            PacketClientInitial
                or PacketDisconnected
                or PacketPlayerJoined
                or PacketPlayerLeft
                or PacketChannelCreated
                or PacketWatchStart
                or PacketWatchStartResponse
                or PacketWatchSnapshotRequest
                or PacketWatchSnapshotResponse
                or PacketWatchResyncRequest
                or PacketWatchResyncSnapshot
                or PacketWatchStop
                or PacketWatchProducerStop
                or PacketWatchEnded
                or PacketWatchSceneCancel
                => PacketPriority.ConnectionControl,

            _ => PacketPriority.General,
        };
}

internal static class PlayerTimelinePacket
{
    private const int SelfPlayerKey = int.MinValue;

    internal static bool IsSelfPlayerKey(int playerKey)
        => playerKey == SelfPlayerKey;

    internal static bool TryGetFrame(
        IContextualPacket packet,
        out int playerKey,
        out PacketPlayerFrame frame
    )
    {
        switch (packet)
        {
        case PacketPlayerFrame direct:
            playerKey = SelfPlayerKey;
            frame = direct;
            return true;
        case PacketContextualPlayerNotification<PacketPlayerFrame> notification:
            playerKey = notification.PlayerID;
            frame = notification.Packet;
            return true;
        default:
            playerKey = 0;
            frame = null!;
            return false;
        }
    }

    internal static bool TryGetBarrierPlayerKey(IContextualPacket packet, out int playerKey)
    {
        switch (packet)
        {
        case PacketPlayerLocationChanged:
        case PacketPlayerLiveState:
        case PacketPlayerChannelMove:
        case PacketWatchTargetRestarting:
            playerKey = SelfPlayerKey;
            return true;
        case PacketPlayerLocationChangedNotification notification:
            playerKey = notification.PlayerID;
            return true;
        case PacketPlayerNotification<PacketPlayerLiveState> notification:
            playerKey = notification.PlayerID;
            return true;
        case PacketPlayerChannelMovedNotification notification:
            playerKey = notification.PlayerID;
            return true;
        case PacketWatchTargetRestartingNotification restarted:
            playerKey = restarted.TargetPlayerID;
            return true;
        default:
            playerKey = 0;
            return false;
        }
    }

    internal static bool TryPromoteFrame(
        IContextualPacket packet,
        out IContextualPacket promoted
    )
    {
        try
        {
            switch (packet)
            {
            case PacketPlayerFrame direct:
                promoted = direct.PromoteToKeyframe();
                return true;
            case PacketContextualPlayerNotification<PacketPlayerFrame> notification:
                promoted = new PacketContextualPlayerNotification<PacketPlayerFrame>(
                    notification.PlayerID,
                    notification.Packet.PromoteToKeyframe()
                );
                return true;
            default:
                promoted = null!;
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            promoted = null!;
            return false;
        }
    }

    internal static bool TryGetTimelinePosition(
        IContextualPacket packet,
        out int playerKey,
        out uint playerEpoch,
        out uint playerSequence
    )
    {
        switch (packet)
        {
        case PacketPlayerFrame frame:
            playerKey = SelfPlayerKey;
            playerEpoch = frame.PlayerEpoch;
            playerSequence = frame.PlayerSequence;
            return true;
        case PacketContextualPlayerNotification<PacketPlayerFrame> notification:
            playerKey = notification.PlayerID;
            playerEpoch = notification.Packet.PlayerEpoch;
            playerSequence = notification.Packet.PlayerSequence;
            return true;
        case PacketPlayerLocationChanged location:
            playerKey = SelfPlayerKey;
            playerEpoch = location.PlayerEpoch;
            playerSequence = location.PlayerSequence;
            return true;
        case PacketPlayerLocationChangedNotification notification:
            playerKey = notification.PlayerID;
            playerEpoch = notification.PlayerEpoch;
            playerSequence = notification.PlayerSequence;
            return true;
        case PacketPlayerLiveState live:
            playerKey = SelfPlayerKey;
            playerEpoch = live.PlayerEpoch;
            playerSequence = live.PlayerSequence;
            return true;
        case PacketPlayerNotification<PacketPlayerLiveState> notification:
            playerKey = notification.PlayerID;
            playerEpoch = notification.Packet.PlayerEpoch;
            playerSequence = notification.Packet.PlayerSequence;
            return true;
        case PacketPlayerChannelMove channel:
            playerKey = SelfPlayerKey;
            playerEpoch = channel.PlayerEpoch;
            playerSequence = channel.PlayerSequence;
            return true;
        case PacketPlayerChannelMovedNotification notification:
            playerKey = notification.PlayerID;
            playerEpoch = notification.PlayerEpoch;
            playerSequence = notification.PlayerSequence;
            return true;
        case PacketWatchTargetRestarting restarting:
            playerKey = SelfPlayerKey;
            playerEpoch = restarting.PlayerEpoch;
            playerSequence = restarting.PlayerSequence;
            return true;
        case PacketWatchTargetRestartingNotification restarted:
            playerKey = restarted.TargetPlayerID;
            playerEpoch = restarted.PlayerEpoch;
            playerSequence = restarted.PlayerSequence;
            return true;
        default:
            playerKey = 0;
            playerEpoch = 0;
            playerSequence = 0;
            return false;
        }
    }

    internal static bool TryGetSceneDependency(
        IContextualPacket packet,
        out int playerKey,
        out uint playerEpoch,
        out uint playerSequence
    )
    {
        switch (packet)
        {
        case PacketWatchSnapshotResponse { IsSuccess: true } response:
            playerKey = SelfPlayerKey;
            playerEpoch = response.Snapshot.PlayerEpoch;
            playerSequence = response.Snapshot.PlayerSequenceWatermark;
            return true;
        case PacketWatchSceneDelta scene:
            playerKey = SelfPlayerKey;
            playerEpoch = scene.Delta.PlayerEpoch;
            playerSequence = scene.Delta.PlayerSequenceWatermark;
            return true;
        case PacketWatchSceneDeltaNotification notification:
            playerKey = notification.TargetPlayerID;
            playerEpoch = notification.Delta.PlayerEpoch;
            playerSequence = notification.Delta.PlayerSequenceWatermark;
            return true;
        case PacketWatchStartResponse { IsSuccess: true } response:
            playerKey = response.TargetPlayerID;
            playerEpoch = response.Snapshot.PlayerEpoch;
            playerSequence = response.Snapshot.PlayerSequenceWatermark;
            return true;
        case PacketWatchResyncSnapshot resync:
            playerKey = resync.TargetPlayerID;
            playerEpoch = resync.Snapshot.PlayerEpoch;
            playerSequence = resync.Snapshot.PlayerSequenceWatermark;
            return true;
        case PacketWatchSceneTransferStart start
            when start.Descriptor.Kind == WatchSceneTransferKind.SnapshotResponse:
            playerKey = SelfPlayerKey;
            playerEpoch = start.Descriptor.PlayerEpoch;
            playerSequence = start.Descriptor.PlayerSequenceWatermark;
            return true;
        case PacketWatchSceneTransferStart start
            when start.Descriptor.Kind == WatchSceneTransferKind.SceneDelta:
            playerKey = SelfPlayerKey;
            playerEpoch = start.Descriptor.PlayerEpoch;
            playerSequence = start.Descriptor.PlayerSequenceWatermark;
            return true;
        case PacketWatchSceneTransferStart start
            when start.Descriptor.Kind == WatchSceneTransferKind.SceneDeltaNotification:
            playerKey = start.Descriptor.TargetPlayerID;
            playerEpoch = start.Descriptor.PlayerEpoch;
            playerSequence = start.Descriptor.PlayerSequenceWatermark;
            return true;
        case PacketWatchSceneTransferStart start
            when start.Descriptor.Kind is WatchSceneTransferKind.StartResponse
                or WatchSceneTransferKind.ResyncSnapshot:
            playerKey = start.Descriptor.TargetPlayerID;
            playerEpoch = start.Descriptor.PlayerEpoch;
            playerSequence = start.Descriptor.PlayerSequenceWatermark;
            return true;
        default:
            playerKey = 0;
            playerEpoch = 0;
            playerSequence = 0;
            return false;
        }
    }

    internal static IEnumerable<(int PlayerKey, uint Epoch, uint Sequence)> GetSnapshotBaselines(
        IContextualPacket packet
    )
    {
        IReadOnlyCollection<PlayerMovedInitialDataWithID>? players = packet switch
        {
            PacketPlayerLocationChangedResponse response => response.Players,
            PacketPlayerChannelMovedResponse response => response.Players,
            _ => null,
        };
        if (players is null)
            yield break;
        foreach (PlayerMovedInitialDataWithID player in players)
            yield return (
                player.PlayerID,
                player.InitialData.PlayerEpoch,
                player.InitialData.PlayerSequence
            );
    }
}

internal enum InactivePlayerPacketDisposition : byte
{
    Unrelated,
    Discard,
    Deliver,
}

internal static class RemotePlayerPacket
{
    internal static InactivePlayerPacketDisposition GetInactiveDisposition(
        IContextualPacket packet,
        out int playerKey
    )
    {
        switch (packet)
        {
        case PacketWatchStartResponse { IsSuccess: true } response:
            playerKey = response.TargetPlayerID;
            return InactivePlayerPacketDisposition.Deliver;
        case PacketWatchSceneTransferStart
            { Descriptor.Kind: WatchSceneTransferKind.StartResponse } start:
            playerKey = start.Descriptor.TargetPlayerID;
            return InactivePlayerPacketDisposition.Deliver;
        }

        if (PlayerTimelinePacket.TryGetTimelinePosition(packet, out playerKey, out _, out _)
            && !PlayerTimelinePacket.IsSelfPlayerKey(playerKey))
            return InactivePlayerPacketDisposition.Discard;
        if (PlayerTimelinePacket.TryGetSceneDependency(packet, out playerKey, out _, out _)
            && !PlayerTimelinePacket.IsSelfPlayerKey(playerKey))
            return InactivePlayerPacketDisposition.Discard;

        switch (packet)
        {
        case PacketPlayerLeft:
            break;
        case PacketPlayerNotification notification:
            playerKey = notification.PlayerID;
            return InactivePlayerPacketDisposition.Discard;
        case PacketPlayerNotification<PacketUpdateGlobalFlag> notification:
            playerKey = notification.PlayerID;
            return InactivePlayerPacketDisposition.Discard;
        case PacketPlayerNotification<PacketCreateFireworks> notification:
            playerKey = notification.PlayerID;
            return InactivePlayerPacketDisposition.Discard;
        case PacketContextualPlayerNotification<PacketPlayerPlayedAudio> notification:
            playerKey = notification.PlayerID;
            return InactivePlayerPacketDisposition.Discard;
        case PacketPlayerGrabPlayer grab:
            playerKey = grab.PlayerID;
            return InactivePlayerPacketDisposition.Discard;
        case PacketPlayerGrabJumpOut jumpOut:
            playerKey = jumpOut.PlayerID;
            return InactivePlayerPacketDisposition.Discard;
        case PacketChatMessage { SourcePlayer: int sourcePlayer }:
            playerKey = sourcePlayer;
            return InactivePlayerPacketDisposition.Discard;
        }

        playerKey = 0;
        return InactivePlayerPacketDisposition.Unrelated;
    }

    internal static bool IsDiscardableForPlayer(IContextualPacket packet, int playerKey)
        => GetInactiveDisposition(packet, out int referencedPlayerKey)
            == InactivePlayerPacketDisposition.Discard
            && referencedPlayerKey == playerKey;
}

internal sealed class ConcurrentPacketPriorityQueue<T>
{
    private enum EntryEligibility
    {
        Ready,
        Waiting,
        Obsolete,
    }

    private sealed class QueueEntry(T item, IContextualPacket? semanticPacket)
    {
        internal T Item { get; set; } = item;
        internal IContextualPacket? SemanticPacket { get; set; } = semanticPacket;
    }

    private readonly object sync = new();
    private readonly LinkedList<QueueEntry>[] lanes =
    [
        new(),
        new(),
        new(),
        new(),
    ];
    private readonly Dictionary<int, LinkedListNode<QueueEntry>> tailFrames = new();
    private readonly Dictionary<int, (uint Epoch, uint Sequence)> sentTimeline = new();
    private readonly Func<int, bool>? isPlayerActive;
    private int count;
    private int nextLane;

    internal ConcurrentPacketPriorityQueue(Func<int, bool>? isPlayerActive = null)
    {
        this.isPlayerActive = isPlayerActive;
    }

    internal int Count
    {
        get { lock (sync) return count; }
    }

    internal bool IsEmpty => Count == 0;

    internal int TimelineCount
    {
        get { lock (sync) return sentTimeline.Count; }
    }

    internal bool Enqueue(PacketPriority priority, T item)
        => Enqueue(
            priority,
            item,
            item is IContextualPacket packet ? packet : null
        );

    internal bool Enqueue(PacketPriority priority, T item, IContextualPacket? semanticPacket)
    {
        lock (sync)
        {
            LinkedList<QueueEntry> lane = lanes[(int)priority];
            if (priority == PacketPriority.PlayerTimeline && semanticPacket is not null)
            {
                if (PlayerTimelinePacket.TryGetFrame(semanticPacket, out int playerKey, out PacketPlayerFrame frame)
                    && tailFrames.TryGetValue(playerKey, out LinkedListNode<QueueEntry>? obsolete)
                    && obsolete.Value.SemanticPacket is not null
                    && PlayerTimelinePacket.TryGetFrame(obsolete.Value.SemanticPacket, out _, out PacketPlayerFrame oldFrame)
                    && oldFrame.PlayerEpoch == frame.PlayerEpoch
                    && PlayerTimelinePacket.TryPromoteFrame(semanticPacket, out IContextualPacket promoted)
                    && TryReplaceItem(item, promoted, out T replaced))
                {
                    obsolete.Value.Item = replaced;
                    obsolete.Value.SemanticPacket = promoted;
                    return false;
                }

                if (PlayerTimelinePacket.TryGetBarrierPlayerKey(semanticPacket, out int barrierPlayerKey))
                    tailFrames.Remove(barrierPlayerKey);
            }

            LinkedListNode<QueueEntry> node = lane.AddLast(new QueueEntry(item, semanticPacket));
            if (priority == PacketPriority.PlayerTimeline
                && semanticPacket is not null
                && PlayerTimelinePacket.TryGetFrame(semanticPacket, out int key, out _))
                tailFrames[key] = node;
            count++;
            return true;
        }
    }

    internal bool TryDequeue(out T item)
        => TryDequeueCore(lanes.Length, out item);

    internal bool TryDequeueNonEntity(out T item)
        => TryDequeueCore((int)PacketPriority.WatchScene, out item);

    internal bool TryPeek(PacketPriority priority, out T item)
    {
        lock (sync)
        {
            LinkedListNode<QueueEntry>? first = lanes[(int)priority].First;
            if (first is null)
            {
                item = default!;
                return false;
            }
            item = first.Value.Item;
            return true;
        }
    }

    internal IReadOnlyList<T> DrainAll()
    {
        lock (sync)
        {
            List<T> drained = new(count);
            foreach (LinkedList<QueueEntry> lane in lanes)
            {
                foreach (QueueEntry entry in lane)
                    drained.Add(entry.Item);
                lane.Clear();
            }

            tailFrames.Clear();
            sentTimeline.Clear();
            count = 0;
            nextLane = 0;
            return drained;
        }
    }

    internal int ForgetPlayer(int playerKey)
    {
        if (PlayerTimelinePacket.IsSelfPlayerKey(playerKey))
            return 0;

        lock (sync)
        {
            sentTimeline.Remove(playerKey);
            tailFrames.Remove(playerKey);

            int removed = 0;
            foreach (LinkedList<QueueEntry> lane in lanes)
            {
                LinkedListNode<QueueEntry>? node = lane.First;
                while (node is not null)
                {
                    LinkedListNode<QueueEntry>? next = node.Next;
                    if (node.Value.SemanticPacket is { } packet
                        && RemotePlayerPacket.IsDiscardableForPlayer(packet, playerKey))
                    {
                        lane.Remove(node);
                        count--;
                        removed++;
                    }
                    node = next;
                }
            }
            return removed;
        }
    }

    private bool TryDequeueCore(int laneCount, out T item)
    {
        lock (sync)
        {
            while (true)
            {
                bool discardedObsolete = false;
                for (int offset = 0; offset < laneCount; offset++)
                {
                    int laneIndex = (nextLane + offset) % laneCount;
                    LinkedList<QueueEntry> lane = lanes[laneIndex];
                    LinkedListNode<QueueEntry>? first = lane.First;
                    if (first is null)
                        continue;

                    EntryEligibility eligibility = first.Value.SemanticPacket is { } candidate
                        ? GetEligibility(candidate)
                        : EntryEligibility.Ready;
                    if (eligibility == EntryEligibility.Waiting)
                        continue;

                    lane.RemoveFirst();
                    count--;
                    nextLane = (laneIndex + 1) % lanes.Length;
                    RemoveTailFrame(first);
                    if (eligibility == EntryEligibility.Obsolete)
                    {
                        discardedObsolete = true;
                        break;
                    }

                    if (first.Value.SemanticPacket is { } dequeuedPacket)
                        TrackDequeuedTimeline(dequeuedPacket);
                    item = first.Value.Item;
                    return true;
                }

                if (!discardedObsolete)
                    break;
            }
        }

        item = default!;
        return false;
    }

    private EntryEligibility GetEligibility(IContextualPacket packet)
    {
        InactivePlayerPacketDisposition inactiveDisposition =
            RemotePlayerPacket.GetInactiveDisposition(packet, out int playerKey);
        if (inactiveDisposition != InactivePlayerPacketDisposition.Unrelated
            && !IsPlayerActive(playerKey))
        {
            return inactiveDisposition == InactivePlayerPacketDisposition.Deliver
                ? EntryEligibility.Ready
                : EntryEligibility.Obsolete;
        }

        if (!PlayerTimelinePacket.TryGetSceneDependency(
            packet,
            out playerKey,
            out uint epoch,
            out uint sequence))
            return EntryEligibility.Ready;
        if (!IsPlayerActive(playerKey))
            return EntryEligibility.Obsolete;
        return sentTimeline.TryGetValue(playerKey, out var sent)
            && (sent.Epoch > epoch || (sent.Epoch == epoch && sent.Sequence >= sequence))
                ? EntryEligibility.Ready
                : EntryEligibility.Waiting;
    }

    private void TrackDequeuedTimeline(IContextualPacket packet)
    {
        if (PlayerTimelinePacket.TryGetTimelinePosition(
            packet,
            out int playerKey,
            out uint epoch,
            out uint sequence))
            TrackTimeline(playerKey, epoch, sequence);
        foreach (var baseline in PlayerTimelinePacket.GetSnapshotBaselines(packet))
            TrackTimeline(baseline.PlayerKey, baseline.Epoch, baseline.Sequence);
    }

    private void TrackTimeline(int playerKey, uint epoch, uint sequence)
    {
        if (IsPlayerActive(playerKey))
            sentTimeline[playerKey] = (epoch, sequence);
    }

    private bool IsPlayerActive(int playerKey)
        => PlayerTimelinePacket.IsSelfPlayerKey(playerKey)
            || isPlayerActive?.Invoke(playerKey) is not false;

    private void RemoveTailFrame(LinkedListNode<QueueEntry> node)
    {
        if (node.Value.SemanticPacket is not null
            && PlayerTimelinePacket.TryGetFrame(node.Value.SemanticPacket, out int key, out _)
            && tailFrames.TryGetValue(key, out LinkedListNode<QueueEntry>? tail)
            && ReferenceEquals(tail, node))
            tailFrames.Remove(key);
    }

    private static bool TryReplaceItem(T original, IContextualPacket packet, out T replaced)
    {
        if (original is IContextualPacket && packet is T typed)
        {
            replaced = typed;
            return true;
        }
        replaced = default!;
        return false;
    }
}
