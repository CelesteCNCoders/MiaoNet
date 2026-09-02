namespace MiaoNet.Shared;

public enum WatchSceneTransferKind : byte
{
    SnapshotResponse,
    SceneDelta,
    StartResponse,
    SceneDeltaNotification,
    ResyncSnapshot,
}

public readonly struct WatchSceneTransferDescriptor : IRefBinarySerializable<WatchSceneTransferDescriptor>
{
    public int TransferID { get; }
    public WatchSceneTransferKind Kind { get; }
    public int TotalLength { get; }
    public ushort FragmentCount { get; }
    public int SceneSequence { get; }
    public uint PlayerEpoch { get; }
    public uint PlayerSequenceWatermark { get; }
    public int RequestID { get; }
    public int SessionID { get; }
    public int TargetPlayerID { get; }

    public WatchSceneTransferDescriptor(
        int transferID,
        WatchSceneTransferKind kind,
        int totalLength,
        ushort fragmentCount,
        int sceneSequence,
        uint playerEpoch,
        uint playerSequenceWatermark,
        int requestID,
        int sessionID,
        int targetPlayerID
    )
    {
        TransferID = transferID;
        Kind = kind;
        TotalLength = totalLength;
        FragmentCount = fragmentCount;
        SceneSequence = sceneSequence;
        PlayerEpoch = playerEpoch;
        PlayerSequenceWatermark = playerSequenceWatermark;
        RequestID = requestID;
        SessionID = sessionID;
        TargetPlayerID = targetPlayerID;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(TransferID);
        writer.Write((byte)Kind);
        writer.Write(TotalLength);
        writer.Write(FragmentCount);
        writer.Write(SceneSequence);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequenceWatermark);
        writer.Write(RequestID);
        writer.Write(SessionID);
        writer.Write(TargetPlayerID);
    }

    public static WatchSceneTransferDescriptor Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadInt32(),
            (WatchSceneTransferKind)reader.ReadByte(),
            reader.ReadInt32(),
            reader.ReadUInt16(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32()
        );
}

public sealed class PacketWatchSceneTransferStart : IContextlessPacket<PacketWatchSceneTransferStart>
{
    public WatchSceneTransferDescriptor Descriptor { get; }

    public PacketWatchSceneTransferStart(WatchSceneTransferDescriptor descriptor)
        => Descriptor = descriptor;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Descriptor);

    public static PacketWatchSceneTransferStart Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<WatchSceneTransferDescriptor>());
}

public sealed class PacketWatchSceneChunk : IContextlessPacket<PacketWatchSceneChunk>
{
    public bool CanBatch => true;

    public int TransferID { get; }
    public ushort FragmentIndex { get; }
    public byte[] Data { get; }

    public PacketWatchSceneChunk(
        int transferID,
        ushort fragmentIndex,
        byte[] data
    ) => (TransferID, FragmentIndex, Data) = (transferID, fragmentIndex, data);

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(TransferID);
        writer.Write(FragmentIndex);
        writer.Write((ushort)Data.Length);
        writer.WriteSpan(Data);
    }

    public static PacketWatchSceneChunk Deserialize(ref RefBinaryReader reader)
    {
        int transferID = reader.ReadInt32();
        ushort fragmentIndex = reader.ReadUInt16();
        int length = reader.ReadUInt16();
        return new(
            transferID,
            fragmentIndex,
            reader.ReadSpan(length).ToArray()
        );
    }
}

public sealed class PacketWatchSceneCancel : IContextlessPacket<PacketWatchSceneCancel>
{
    public int TransferID { get; }

    public PacketWatchSceneCancel(int transferID)
        => TransferID = transferID;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(TransferID);

    public static PacketWatchSceneCancel Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32());
}

internal static class WatchSceneFragmenter
{
    private sealed class FragmentCacheEntry(IReadOnlyList<IContextualPacket> packets)
    {
        internal IReadOnlyList<IContextualPacket> Packets { get; } = packets;
    }

    internal const int FragmentSize = 8 * 1024;
    internal const int MaxLogicalPayloadSize = ushort.MaxValue;
    internal const int MaxFragmentCount = 8;
    private static int nextTransferID;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        IContextualPacket,
        FragmentCacheEntry
    > fragmentedPacketCache = new();

    internal static bool TryFragment(
        IContextualPacket packet,
        out IReadOnlyList<IContextualPacket> fragments
    )
    {
        if (fragmentedPacketCache.TryGetValue(packet, out FragmentCacheEntry? cached))
        {
            fragments = cached.Packets;
            return true;
        }

        if (!TryGetPayload(packet, out WatchSceneTransferKind kind, out object state,
            out int requestID, out int sessionID, out int targetPlayerID))
        {
            fragments = [];
            return false;
        }

        ReadOnlyMemory<byte> payload;
        int sceneSequence;
        uint playerEpoch;
        uint playerSequenceWatermark;
        if (state is WatchSceneSnapshot snapshot)
        {
            payload = snapshot.EncodedPayload;
            sceneSequence = snapshot.Sequence;
            playerEpoch = snapshot.PlayerEpoch;
            playerSequenceWatermark = snapshot.PlayerSequenceWatermark;
        }
        else
        {
            WatchSceneDelta delta = (WatchSceneDelta)state;
            payload = delta.EncodedPayload;
            sceneSequence = delta.Sequence;
            playerEpoch = delta.PlayerEpoch;
            playerSequenceWatermark = delta.PlayerSequenceWatermark;
        }

        int length = payload.Length;
        if (length <= FragmentSize)
        {
            fragments = [];
            return false;
        }
        if (length > MaxLogicalPayloadSize)
            throw new InvalidDataException($"Watch scene payload {length} exceeds {MaxLogicalPayloadSize} bytes.");

        int count = (length + FragmentSize - 1) / FragmentSize;
        if (count > MaxFragmentCount)
            throw new InvalidDataException($"Watch scene payload requires {count} fragments.");

        int transferID = Interlocked.Increment(ref nextTransferID);
        if (transferID == 0)
            transferID = Interlocked.Increment(ref nextTransferID);
        WatchSceneTransferDescriptor descriptor = new(
            transferID,
            kind,
            length,
            (ushort)count,
            sceneSequence,
            playerEpoch,
            playerSequenceWatermark,
            requestID,
            sessionID,
            targetPlayerID
        );

        IContextualPacket[] packets = new IContextualPacket[count + 1];
        packets[0] = new PacketWatchSceneTransferStart(descriptor);
        for (int i = 0; i < count; i++)
        {
            int offset = i * FragmentSize;
            int fragmentLength = Math.Min(FragmentSize, length - offset);
            packets[i + 1] = new PacketWatchSceneChunk(
                transferID,
                (ushort)i,
                payload.Span.Slice(offset, fragmentLength).ToArray()
            );
        }
        FragmentCacheEntry cacheEntry = fragmentedPacketCache.GetValue(
            packet,
            _ => new FragmentCacheEntry(packets)
        );
        fragments = cacheEntry.Packets;
        return true;
    }

    private static bool TryGetPayload(
        IContextualPacket packet,
        out WatchSceneTransferKind kind,
        out object state,
        out int requestID,
        out int sessionID,
        out int targetPlayerID
    )
    {
        requestID = 0;
        sessionID = 0;
        targetPlayerID = 0;
        switch (packet)
        {
        case PacketWatchSnapshotResponse { IsSuccess: true } response:
            kind = WatchSceneTransferKind.SnapshotResponse;
            state = response.Snapshot;
            requestID = response.RequestID;
            return true;
        case PacketWatchSceneDelta delta:
            kind = WatchSceneTransferKind.SceneDelta;
            state = delta.Delta;
            return true;
        case PacketWatchStartResponse { IsSuccess: true } response:
            kind = WatchSceneTransferKind.StartResponse;
            state = response.Snapshot;
            requestID = response.RequestID;
            sessionID = response.SessionID;
            targetPlayerID = response.TargetPlayerID;
            return true;
        case PacketWatchSceneDeltaNotification notification:
            kind = WatchSceneTransferKind.SceneDeltaNotification;
            state = notification.Delta;
            sessionID = notification.SessionID;
            targetPlayerID = notification.TargetPlayerID;
            return true;
        case PacketWatchResyncSnapshot resync:
            kind = WatchSceneTransferKind.ResyncSnapshot;
            state = resync.Snapshot;
            sessionID = resync.SessionID;
            targetPlayerID = resync.TargetPlayerID;
            return true;
        default:
            kind = default;
            state = null!;
            return false;
        }
    }
}

internal sealed class WatchSceneTransferReceiver
{
    private const int MaxPendingTransfers = 8;
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(3);

    private sealed class Pending(WatchSceneTransferDescriptor descriptor)
    {
        internal WatchSceneTransferDescriptor Descriptor { get; } = descriptor;
        internal byte[]?[] Fragments { get; } = new byte[descriptor.FragmentCount][];
        internal DateTime ExpiresAt { get; } = DateTime.UtcNow + TransferTimeout;
        internal int ReceivedCount { get; set; }
        internal int ReceivedLength { get; set; }

        // ExpireAsync can retain this Pending after completion/cancellation. It
        // must not retain the much larger fragment buffers until its delay ends.
        internal void ReleaseFragments() => Array.Clear(Fragments);
    }

    private readonly Dictionary<int, Pending> pending = new();
    private readonly object sync = new();

    internal bool TryAccept(IContextualPacket packet, out IContextualPacket? logicalPacket)
    {
        lock (sync)
        {
            logicalPacket = null;
            RemoveExpired();
            switch (packet)
            {
            case PacketWatchSceneTransferStart start:
                AcceptStart(start.Descriptor);
                return true;
            case PacketWatchSceneChunk chunk:
                logicalPacket = AcceptChunk(chunk);
                return true;
            case PacketWatchSceneCancel cancel:
                if (cancel.TransferID == 0)
                    ClearAll();
                else
                    RemoveTransfer(cancel.TransferID);
                return true;
            default:
                return false;
            }
        }
    }

    internal void Clear()
    {
        lock (sync)
            ClearAll();
    }

    private void ClearAll()
    {
        foreach (Pending transfer in pending.Values)
            transfer.ReleaseFragments();
        pending.Clear();
    }

    private void RemoveTransfer(int transferID)
    {
        if (pending.Remove(transferID, out Pending? transfer))
            transfer.ReleaseFragments();
    }

    internal void ClearForTarget(int targetPlayerID)
    {
        lock (sync)
        {
            foreach (int transferID in pending
                .Where(pair => pair.Value.Descriptor.TargetPlayerID == targetPlayerID
                    && !IsRequiredCompletion(pair.Value.Descriptor))
                .Select(pair => pair.Key)
                .ToArray())
                RemoveTransfer(transferID);
        }
    }

    internal void ClearDiscardable()
    {
        lock (sync)
        {
            foreach (int transferID in pending
                .Where(pair => !IsRequiredCompletion(pair.Value.Descriptor))
                .Select(pair => pair.Key)
                .ToArray())
                RemoveTransfer(transferID);
        }
    }

    private void AcceptStart(WatchSceneTransferDescriptor descriptor)
    {
        int expectedCount = (descriptor.TotalLength + WatchSceneFragmenter.FragmentSize - 1)
            / WatchSceneFragmenter.FragmentSize;
        if (descriptor.TransferID == 0
            || !Enum.IsDefined(descriptor.Kind)
            || descriptor.TotalLength <= WatchSceneFragmenter.FragmentSize
            || descriptor.TotalLength > WatchSceneFragmenter.MaxLogicalPayloadSize
            || descriptor.FragmentCount == 0
            || descriptor.FragmentCount > WatchSceneFragmenter.MaxFragmentCount
            || descriptor.FragmentCount != expectedCount
            || pending.ContainsKey(descriptor.TransferID)
            || pending.Count >= MaxPendingTransfers
            || pending.Values.Any(value =>
                value.Descriptor.Kind == descriptor.Kind
                && value.Descriptor.SessionID == descriptor.SessionID))
            throw new InvalidDataException("Invalid or conflicting Watch scene transfer descriptor.");

        Pending transfer = new(descriptor);
        pending.Add(descriptor.TransferID, transfer);
        _ = ExpireAsync(descriptor.TransferID, transfer);
    }

    private IContextualPacket? AcceptChunk(PacketWatchSceneChunk chunk)
    {
        // Unknown chunks may be the tail of a transfer cancelled by a higher-priority
        // lifecycle packet. They are safe to discard because no state was applied.
        if (!pending.TryGetValue(chunk.TransferID, out Pending? transfer))
            return null;
        bool terminal = false;
        try
        {
            WatchSceneTransferDescriptor descriptor = transfer.Descriptor;
            if (chunk.FragmentIndex >= descriptor.FragmentCount
                || transfer.Fragments[chunk.FragmentIndex] is not null)
                throw new InvalidDataException("Duplicate or out-of-range Watch scene fragment.");

            int expectedLength = chunk.FragmentIndex + 1 == descriptor.FragmentCount
                ? descriptor.TotalLength - chunk.FragmentIndex * WatchSceneFragmenter.FragmentSize
                : WatchSceneFragmenter.FragmentSize;
            if (chunk.Data.Length != expectedLength)
                throw new InvalidDataException("Watch scene fragment length mismatch.");

            transfer.Fragments[chunk.FragmentIndex] = chunk.Data;
            transfer.ReceivedCount++;
            transfer.ReceivedLength += chunk.Data.Length;
            if (transfer.ReceivedCount != descriptor.FragmentCount)
                return null;

            terminal = true;
            if (transfer.ReceivedLength != descriptor.TotalLength)
                throw new InvalidDataException("Watch scene transfer total length mismatch.");

            byte[] payload = new byte[descriptor.TotalLength];
            int offset = 0;
            foreach (byte[] fragment in transfer.Fragments!)
            {
                fragment.CopyTo(payload, offset);
                offset += fragment.Length;
            }
            RefBinaryReader reader = new(payload);
            IContextualPacket result = Reconstruct(descriptor, ref reader);
            if (reader.BytesLeft != 0)
                throw new InvalidDataException("Watch scene transfer has trailing bytes.");
            return result;
        }
        catch
        {
            terminal = true;
            throw;
        }
        finally
        {
            if (terminal)
                RemoveTransfer(chunk.TransferID);
        }
    }

    private static bool IsRequiredCompletion(WatchSceneTransferDescriptor descriptor)
        => descriptor.Kind == WatchSceneTransferKind.StartResponse;

    private static IContextualPacket Reconstruct(
        WatchSceneTransferDescriptor descriptor,
        ref RefBinaryReader reader
    )
    {
        IContextualPacket packet;
        int sequence;
        uint epoch;
        uint watermark;
        if (descriptor.Kind is WatchSceneTransferKind.SnapshotResponse
            or WatchSceneTransferKind.StartResponse
            or WatchSceneTransferKind.ResyncSnapshot)
        {
            WatchSceneSnapshot snapshot = reader.Read<WatchSceneSnapshot>();
            sequence = snapshot.Sequence;
            epoch = snapshot.PlayerEpoch;
            watermark = snapshot.PlayerSequenceWatermark;
            packet = descriptor.Kind switch
            {
                WatchSceneTransferKind.SnapshotResponse => new PacketWatchSnapshotResponse(
                    WatchSnapshotResult.Success,
                    snapshot
                ) { RequestID = descriptor.RequestID },
                WatchSceneTransferKind.StartResponse => new PacketWatchStartResponse(
                    WatchStartResult.Success,
                    descriptor.SessionID,
                    snapshot,
                    descriptor.TargetPlayerID
                ) { RequestID = descriptor.RequestID },
                _ => new PacketWatchResyncSnapshot(
                    descriptor.SessionID,
                    descriptor.TargetPlayerID,
                    snapshot
                ),
            };
        }
        else
        {
            WatchSceneDelta delta = reader.Read<WatchSceneDelta>();
            sequence = delta.Sequence;
            epoch = delta.PlayerEpoch;
            watermark = delta.PlayerSequenceWatermark;
            packet = descriptor.Kind == WatchSceneTransferKind.SceneDelta
                ? new PacketWatchSceneDelta(delta)
                : new PacketWatchSceneDeltaNotification(
                    descriptor.SessionID,
                    descriptor.TargetPlayerID,
                    delta
                );
        }

        if (sequence != descriptor.SceneSequence
            || epoch != descriptor.PlayerEpoch
            || watermark != descriptor.PlayerSequenceWatermark)
            throw new InvalidDataException("Watch scene transfer descriptor does not match its payload.");
        return packet;
    }

    private void RemoveExpired()
    {
        DateTime now = DateTime.UtcNow;
        foreach (int transferID in pending
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray())
            RemoveTransfer(transferID);
    }

    private async Task ExpireAsync(int transferID, Pending expected)
    {
        await Task.Delay(TransferTimeout);
        lock (sync)
        {
            if (pending.TryGetValue(transferID, out Pending? current)
                && ReferenceEquals(current, expected))
                RemoveTransfer(transferID);
        }
    }
}
