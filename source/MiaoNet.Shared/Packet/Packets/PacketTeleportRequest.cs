using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// client to server
public sealed class PacketTeleportRequest :
    PacketRequest<PacketTeleportResponse>,
    IContextlessPacket<PacketTeleportRequest>
{
    public int TargetPlayerID { get; }

    public PacketTeleportRequest(int targetPlayerID)
    {
        TargetPlayerID = targetPlayerID;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write(TargetPlayerID);
    }

    public static PacketTeleportRequest Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.ReadInt32();
        return new(reader.ReadInt32()) { RequestID = reqID };
    }
}

// server to client
public sealed class PacketTeleportResponse :
    PacketResponse,
    IContextlessPacket<PacketTeleportResponse>
{
    public enum TeleportFailedReason
    {
        None = 0,
        NoSuchPlayer = 1,
        OtherDenied = 2
    }

    [MemberNotNullWhen(false, nameof(Session))]
    public bool IsFailed => FailedReason != TeleportFailedReason.None;

    public TeleportFailedReason FailedReason { get; }

    public PlayerSessionData? Session { get; }

    public PacketTeleportResponse(TeleportFailedReason failedReason, PlayerSessionData? session)
    {
        FailedReason = failedReason;
        Session = session;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write((byte)FailedReason);
        if (!IsFailed)
            writer.Write(Session);
    }

    public static PacketTeleportResponse Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.ReadInt32();
        TeleportFailedReason failedReason = (TeleportFailedReason)reader.ReadByte();
        PlayerSessionData? session = failedReason == TeleportFailedReason.None
            ? reader.Read<PlayerSessionData>()
            : null;
        return new(failedReason, session) { RequestID = reqID };
    }
}