using System.Threading.Channels;
using MiaoNet.Shared;

namespace MiaoNet.Server;

internal sealed class PriorityPacketChannel
{
    private sealed class BoundedLane
    {
        private readonly object sync = new();
        private readonly int capacity;
        private readonly bool coalescePlayerFrames;
        private readonly LinkedList<IContextualPacket> packets = new();
        private readonly Dictionary<int, LinkedListNode<IContextualPacket>> tailFrames = new();
        private readonly Channel<byte> spaceAvailable = Channel.CreateUnbounded<byte>();
        private bool completed;

        internal BoundedLane(int capacity, bool coalescePlayerFrames)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            this.capacity = capacity;
            this.coalescePlayerFrames = coalescePlayerFrames;
        }

        internal bool TryWrite(IContextualPacket packet, out bool added)
        {
            lock (sync)
            {
                if (completed)
                {
                    added = false;
                    return false;
                }

                if (coalescePlayerFrames)
                {
                    if (PlayerTimelinePacket.TryGetFrame(packet, out int playerKey, out PacketPlayerFrame frame)
                        && tailFrames.TryGetValue(playerKey, out LinkedListNode<IContextualPacket>? obsolete)
                        && PlayerTimelinePacket.TryGetFrame(obsolete.Value, out _, out PacketPlayerFrame oldFrame)
                        && oldFrame.PlayerEpoch == frame.PlayerEpoch
                        && PlayerTimelinePacket.TryPromoteFrame(packet, out IContextualPacket promoted))
                    {
                        obsolete.Value = promoted;
                        added = false;
                        return true;
                    }

                    if (PlayerTimelinePacket.TryGetBarrierPlayerKey(packet, out int barrierPlayerKey))
                        tailFrames.Remove(barrierPlayerKey);
                }

                if (packets.Count >= capacity)
                {
                    added = false;
                    return false;
                }

                LinkedListNode<IContextualPacket> node = packets.AddLast(packet);
                if (coalescePlayerFrames
                    && PlayerTimelinePacket.TryGetFrame(packet, out int key, out _))
                    tailFrames[key] = node;
                added = true;
                return true;
            }
        }

        internal bool TryRead(out IContextualPacket packet)
        {
            lock (sync)
            {
                LinkedListNode<IContextualPacket>? first = packets.First;
                if (first is null)
                {
                    packet = null!;
                    return false;
                }
                packets.RemoveFirst();
                if (coalescePlayerFrames
                    && PlayerTimelinePacket.TryGetFrame(first.Value, out int key, out _)
                    && tailFrames.TryGetValue(key, out LinkedListNode<IContextualPacket>? tail)
                    && ReferenceEquals(tail, first))
                    tailFrames.Remove(key);
                packet = first.Value;
            }
            spaceAvailable.Writer.TryWrite(0);
            return true;
        }

        internal bool TryPeek(out IContextualPacket packet)
        {
            lock (sync)
            {
                if (packets.First is not { } first)
                {
                    packet = null!;
                    return false;
                }
                packet = first.Value;
                return true;
            }
        }

        internal async ValueTask WaitForSpaceAsync(CancellationToken token)
            => await spaceAvailable.Reader.ReadAsync(token);

        internal void Complete()
        {
            lock (sync)
                completed = true;
            spaceAvailable.Writer.TryComplete();
        }
    }

    private readonly BoundedLane[] lanes;
    private readonly Channel<byte> ready = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private int nextLane;
    private int blockedReadyCount;
    private readonly Dictionary<int, (uint Epoch, uint Sequence)> sentTimeline = new();

    internal PriorityPacketChannel(
        int controlCapacity,
        int playerFrameCapacity,
        int generalCapacity,
        int watchEntityCapacity
    )
    {
        lanes =
        [
            new(controlCapacity, false),
            new(playerFrameCapacity, true),
            new(generalCapacity, false),
            new(watchEntityCapacity, false),
        ];
    }

    internal bool TryWrite(IContextualPacket packet)
        => TryWrite(packet, out _);

    internal bool TryWrite(IContextualPacket packet, out bool coalesced)
    {
        BoundedLane lane = GetLane(packet);
        if (!lane.TryWrite(packet, out bool added))
        {
            coalesced = false;
            return false;
        }
        coalesced = !added;
        if (added && !ready.Writer.TryWrite(0))
            throw new InvalidOperationException("The packet-ready channel was unexpectedly closed.");
        return true;
    }

    internal async ValueTask<bool> WriteAsync(IContextualPacket packet, CancellationToken token)
    {
        BoundedLane lane = GetLane(packet);
        while (true)
        {
            if (lane.TryWrite(packet, out bool added))
            {
                if (added && !ready.Writer.TryWrite(0))
                    throw new ChannelClosedException();
                return !added;
            }
            await lane.WaitForSpaceAsync(token);
        }
    }

    internal bool TryRead(out IContextualPacket packet)
    {
        int consumedReady = 0;
        while (ready.Reader.TryRead(out _))
        {
            consumedReady++;
            for (int offset = 0; offset < lanes.Length; offset++)
            {
                int laneIndex = (nextLane + offset) % lanes.Length;
                if (!lanes[laneIndex].TryPeek(out IContextualPacket candidate)
                    || !SceneDependencySatisfied(candidate))
                    continue;
                if (!lanes[laneIndex].TryRead(out packet!))
                    continue;
                nextLane = (laneIndex + 1) % lanes.Length;
                bool dependencyAdvanced = TrackDequeuedTimeline(packet);
                if (dependencyAdvanced && blockedReadyCount > 0)
                {
                    for (int i = 0; i < blockedReadyCount; i++)
                        ready.Writer.TryWrite(0);
                    blockedReadyCount = 0;
                }
                return true;
            }
        }

        blockedReadyCount += consumedReady;
        packet = null!;
        return false;
    }

    internal async ValueTask<bool> WaitToReadAsync(CancellationToken token)
        => await ready.Reader.WaitToReadAsync(token);

    internal void Complete()
    {
        foreach (BoundedLane lane in lanes)
            lane.Complete();
        ready.Writer.TryComplete();
    }

    private BoundedLane GetLane(IContextualPacket packet)
        => lanes[(int)PacketPriorityClassifier.Classify(packet)];

    private bool SceneDependencySatisfied(IContextualPacket packet)
    {
        if (!PlayerTimelinePacket.TryGetSceneDependency(
            packet,
            out int playerKey,
            out uint epoch,
            out uint sequence))
            return true;
        return sentTimeline.TryGetValue(playerKey, out var sent)
            && (sent.Epoch > epoch || (sent.Epoch == epoch && sent.Sequence >= sequence));
    }

    private bool TrackDequeuedTimeline(IContextualPacket packet)
    {
        bool changed = false;
        if (PlayerTimelinePacket.TryGetTimelinePosition(
            packet,
            out int playerKey,
            out uint epoch,
            out uint sequence))
        {
            sentTimeline[playerKey] = (epoch, sequence);
            changed = true;
        }
        foreach (var baseline in PlayerTimelinePacket.GetSnapshotBaselines(packet))
        {
            sentTimeline[baseline.PlayerKey] = (baseline.Epoch, baseline.Sequence);
            changed = true;
        }
        return changed;
    }
}
