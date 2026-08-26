using System.Collections.Concurrent;

namespace MiaoNet.Shared;

internal enum PacketPriority : byte
{
    Control,
    PlayerFrame,
    General,
    WatchEntity,
}

internal static class PacketPriorityClassifier
{
    internal static PacketPriority Classify(IContextualPacket packet)
        => packet switch
        {
            PacketPlayerFrame
                or PacketContextualPlayerNotification<PacketPlayerFrame>
                => PacketPriority.PlayerFrame,

            PacketWatchSceneDelta
                or PacketWatchSceneDeltaNotification
                => PacketPriority.WatchEntity,

            PacketClientInitial
                or PacketDisconnected
                or PacketPlayerJoined
                or PacketPlayerLeft
                or PacketPlayerLiveState
                or PacketPlayerNotification<PacketPlayerLiveState>
                or PacketPlayerLocationChanged
                or PacketPlayerLocationChangedNotification
                or PacketPlayerLocationChangedResponse
                or PacketPlayerChannelMove
                or PacketPlayerChannelMovedResponse
                or PacketPlayerChannelMovedNotification
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
                => PacketPriority.Control,

            _ => PacketPriority.General,
        };
}

internal sealed class ConcurrentPacketPriorityQueue<T>
{
    private readonly ConcurrentQueue<T> control = new();
    private readonly ConcurrentQueue<T> playerFrame = new();
    private readonly ConcurrentQueue<T> general = new();
    private readonly ConcurrentQueue<T> watchEntity = new();

    internal int Count
        => control.Count + playerFrame.Count + general.Count + watchEntity.Count;

    internal bool IsEmpty
        => control.IsEmpty && playerFrame.IsEmpty && general.IsEmpty && watchEntity.IsEmpty;

    internal void Enqueue(PacketPriority priority, T item)
        => GetQueue(priority).Enqueue(item);

    internal bool TryDequeue(out T item)
        => control.TryDequeue(out item!)
            || playerFrame.TryDequeue(out item!)
            || general.TryDequeue(out item!)
            || watchEntity.TryDequeue(out item!);

    internal bool TryDequeueNonEntity(out T item)
        => control.TryDequeue(out item!)
            || playerFrame.TryDequeue(out item!)
            || general.TryDequeue(out item!);

    internal bool TryPeek(PacketPriority priority, out T item)
        => GetQueue(priority).TryPeek(out item!);

    private ConcurrentQueue<T> GetQueue(PacketPriority priority)
        => priority switch
        {
            PacketPriority.Control => control,
            PacketPriority.PlayerFrame => playerFrame,
            PacketPriority.General => general,
            PacketPriority.WatchEntity => watchEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(priority)),
        };
}
