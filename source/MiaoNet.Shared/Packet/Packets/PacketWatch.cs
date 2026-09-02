using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

public enum WatchStartResult : byte
{
    Success,
    NoSuchPlayer,
    SelfTarget,
    DifferentChannel,
    DifferentMap,
    TargetIsWatching,
    InvalidState,
    TargetUnavailable,
    UnsupportedProtocol,
}

public enum WatchSnapshotResult : byte
{
    Success,
    Unavailable,
    LocationChanged
}

public enum WatchEndReason : byte
{
    TargetDisconnected,
    ChannelChanged,
    LocationChanged,
    TargetBeganWatching,
    InvalidSession
}

public enum WatchTargetRestartKind : byte
{
    RestartChapter,
    GoldenBerryRestart,
}

// client to server
public sealed class PacketWatchStart :
    PacketRequest<PacketWatchStartResponse>,
    IContextlessPacket<PacketWatchStart>
{
    public int TargetPlayerID { get; }

    public PacketWatchStart(int targetPlayerID)
    {
        TargetPlayerID = targetPlayerID;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write(TargetPlayerID);
    }

    public static PacketWatchStart Deserialize(ref RefBinaryReader reader)
    {
        int requestID = reader.ReadInt32();
        return new(reader.ReadInt32()) { RequestID = requestID };
    }
}

// server to client
public sealed class PacketWatchStartResponse :
    PacketResponse,
    IContextlessPacket<PacketWatchStartResponse>
{
    public WatchStartResult Result { get; }

    public int SessionID { get; }

    public int TargetPlayerID { get; }

    public WatchSceneSnapshot? Snapshot { get; }

    [MemberNotNullWhen(true, nameof(Snapshot))]
    public bool IsSuccess => Result == WatchStartResult.Success;

    public PacketWatchStartResponse(
        WatchStartResult result,
        int sessionID,
        WatchSceneSnapshot? snapshot,
        int targetPlayerID = 0
    )
    {
        Result = result;
        SessionID = sessionID;
        Snapshot = snapshot;
        TargetPlayerID = targetPlayerID;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write((byte)Result);
        if (IsSuccess)
        {
            writer.Write(SessionID);
            writer.Write(TargetPlayerID);
            writer.WriteSpan(Snapshot.EncodedPayload.Span);
        }
    }

    public static PacketWatchStartResponse Deserialize(ref RefBinaryReader reader)
    {
        int requestID = reader.ReadInt32();
        WatchStartResult result = (WatchStartResult)reader.ReadByte();
        return result == WatchStartResult.Success
            ? DeserializeSuccess(ref reader, requestID, result)
            : new(result, 0, null) { RequestID = requestID };
    }

    private static PacketWatchStartResponse DeserializeSuccess(
        ref RefBinaryReader reader,
        int requestID,
        WatchStartResult result
    )
    {
        int sessionID = reader.ReadInt32();
        int targetPlayerID = reader.ReadInt32();
        WatchSceneSnapshot snapshot = reader.Read<WatchSceneSnapshot>();
        return new(result, sessionID, snapshot, targetPlayerID) { RequestID = requestID };
    }
}

// server to client
public sealed class PacketWatchSnapshotRequest :
    PacketRequest<PacketWatchSnapshotResponse>,
    IContextlessPacket<PacketWatchSnapshotRequest>
{
    public int SessionID { get; }

    public PlayerLocation ExpectedLocation { get; }

    public PacketWatchSnapshotRequest(int sessionID, PlayerLocation expectedLocation)
    {
        SessionID = sessionID;
        ExpectedLocation = expectedLocation;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write(SessionID);
        writer.Write(ExpectedLocation);
    }

    public static PacketWatchSnapshotRequest Deserialize(ref RefBinaryReader reader)
    {
        int requestID = reader.ReadInt32();
        return new(reader.ReadInt32(), reader.Read<PlayerLocation>()) { RequestID = requestID };
    }
}

// client to server
public sealed class PacketWatchSnapshotResponse :
    PacketResponse,
    IContextlessPacket<PacketWatchSnapshotResponse>
{
    public WatchSnapshotResult Result { get; }

    public WatchSceneSnapshot? Snapshot { get; }

    [MemberNotNullWhen(true, nameof(Snapshot))]
    public bool IsSuccess => Result == WatchSnapshotResult.Success;

    public PacketWatchSnapshotResponse(WatchSnapshotResult result, WatchSceneSnapshot? snapshot)
    {
        Result = result;
        Snapshot = snapshot;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write((byte)Result);
        if (IsSuccess)
            writer.WriteSpan(Snapshot.EncodedPayload.Span);
    }

    public static PacketWatchSnapshotResponse Deserialize(ref RefBinaryReader reader)
    {
        int requestID = reader.ReadInt32();
        WatchSnapshotResult result = (WatchSnapshotResult)reader.ReadByte();
        return result == WatchSnapshotResult.Success
            ? new(result, reader.Read<WatchSceneSnapshot>()) { RequestID = requestID }
            : new(result, null) { RequestID = requestID };
    }
}

// client to server
public sealed class PacketWatchSceneDelta : IContextlessPacket<PacketWatchSceneDelta>
{
    public WatchSceneDelta Delta { get; }

    public PacketWatchSceneDelta(WatchSceneDelta delta)
    {
        Delta = delta;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.WriteSpan(Delta.EncodedPayload.Span);

    public static PacketWatchSceneDelta Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<WatchSceneDelta>());
}

// server to client
public sealed class PacketWatchSceneDeltaNotification : IContextlessPacket<PacketWatchSceneDeltaNotification>
{
    public int SessionID { get; }

    public int TargetPlayerID { get; }

    public WatchSceneDelta Delta { get; }

    public PacketWatchSceneDeltaNotification(int sessionID, int targetPlayerID, WatchSceneDelta delta)
    {
        SessionID = sessionID;
        TargetPlayerID = targetPlayerID;
        Delta = delta;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SessionID);
        writer.Write(TargetPlayerID);
        writer.WriteSpan(Delta.EncodedPayload.Span);
    }

    public static PacketWatchSceneDeltaNotification Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(), reader.Read<WatchSceneDelta>());
}

// client to server
public sealed class PacketWatchResyncRequest : IContextlessPacket<PacketWatchResyncRequest>
{
    public int SessionID { get; }

    public int LastAppliedSequence { get; }

    public PacketWatchResyncRequest(int sessionID, int lastAppliedSequence)
    {
        SessionID = sessionID;
        LastAppliedSequence = lastAppliedSequence;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SessionID);
        writer.Write(LastAppliedSequence);
    }

    public static PacketWatchResyncRequest Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32());
}

// server to client
public sealed class PacketWatchResyncSnapshot : IContextlessPacket<PacketWatchResyncSnapshot>
{
    public int SessionID { get; }

    public int TargetPlayerID { get; }

    public WatchSceneSnapshot Snapshot { get; }

    public PacketWatchResyncSnapshot(
        int sessionID,
        int targetPlayerID,
        WatchSceneSnapshot snapshot
    )
    {
        SessionID = sessionID;
        TargetPlayerID = targetPlayerID;
        Snapshot = snapshot;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SessionID);
        writer.Write(TargetPlayerID);
        writer.WriteSpan(Snapshot.EncodedPayload.Span);
    }

    public static PacketWatchResyncSnapshot Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(), reader.Read<WatchSceneSnapshot>());
}

// client to server
public sealed class PacketWatchStop : IContextlessPacket<PacketWatchStop>
{
    public int SessionID { get; }

    public PacketWatchStop(int sessionID)
    {
        SessionID = sessionID;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(SessionID);

    public static PacketWatchStop Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32());
}

// bidirectional
public sealed class PacketWatchProducerStop : IContextlessPacket<PacketWatchProducerStop>
{
    public int SessionID { get; }

    public PacketWatchProducerStop(int sessionID)
    {
        SessionID = sessionID;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(SessionID);

    public static PacketWatchProducerStop Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32());
}

// server to client
public sealed class PacketWatchEnded : IContextlessPacket<PacketWatchEnded>
{
    public int SessionID { get; }

    public WatchEndReason Reason { get; }

    public PacketWatchEnded(int sessionID, WatchEndReason reason)
    {
        SessionID = sessionID;
        Reason = reason;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SessionID);
        writer.Write((byte)Reason);
    }

    public static PacketWatchEnded Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), (WatchEndReason)reader.ReadByte());
}

// watched client to server
public sealed class PacketWatchTargetRestarting : IContextlessPacket<PacketWatchTargetRestarting>
{
    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public uint EmptyLocationEpoch { get; }

    public WatchTargetRestartKind Kind { get; }

    public PacketWatchTargetRestarting(
        uint playerEpoch,
        uint playerSequence,
        uint emptyLocationEpoch,
        WatchTargetRestartKind kind
    )
    {
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        EmptyLocationEpoch = emptyLocationEpoch;
        Kind = kind;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write(EmptyLocationEpoch);
        writer.Write((byte)Kind);
    }

    public static PacketWatchTargetRestarting Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            (WatchTargetRestartKind)reader.ReadByte()
        );
}

// server to watcher
public sealed class PacketWatchTargetRestartingNotification :
    IContextlessPacket<PacketWatchTargetRestartingNotification>
{
    public int SessionID { get; }

    public int TargetPlayerID { get; }

    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public WatchTargetRestartKind Kind { get; }

    public PacketWatchTargetRestartingNotification(
        int sessionID,
        int targetPlayerID,
        uint playerEpoch,
        uint playerSequence,
        WatchTargetRestartKind kind
    )
    {
        SessionID = sessionID;
        TargetPlayerID = targetPlayerID;
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Kind = kind;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SessionID);
        writer.Write(TargetPlayerID);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write((byte)Kind);
    }

    public static PacketWatchTargetRestartingNotification Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            (WatchTargetRestartKind)reader.ReadByte()
        );
}
