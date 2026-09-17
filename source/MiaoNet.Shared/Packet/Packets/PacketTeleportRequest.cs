using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// client to server
public sealed class PacketTeleportRequest :
    IPacketRequest<PacketTeleportResponse>,
    IContextlessPacket<PacketTeleportRequest>
{
    public int TargetPlayerID { get; }

    public PacketTeleportRequest(int targetPlayerID)
    {
        TargetPlayerID = targetPlayerID;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(TargetPlayerID);
    }

    public static PacketTeleportRequest Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.Read7BitEncodedInt());
    }
}

// server to client
public sealed class PacketTeleportResponse :
    IPacketResponse,
    IContextlessPacket<PacketTeleportResponse>
{
    public enum TeleportFailedReason
    {
        None,
        NoSuchPlayer,
        OtherDenied,
        OtherDoesNotResponse // TODO
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

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)FailedReason);
        if (!IsFailed)
            writer.Write(Session);
    }

    public static PacketTeleportResponse Deserialize(ref RefBinaryReader reader)
    {
        TeleportFailedReason failedReason = (TeleportFailedReason)reader.ReadByte();
        PlayerSessionData? session = failedReason == TeleportFailedReason.None
            ? reader.Read<PlayerSessionData>()
            : null;
        return new(failedReason, session);
    }
}
