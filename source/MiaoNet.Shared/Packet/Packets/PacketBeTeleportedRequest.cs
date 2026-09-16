using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// server to client
public sealed class PacketBeTeleportedRequest :
    PacketRequest<PacketBeTeleportedResponse>,
    IContextlessPacket<PacketBeTeleportedRequest>
{
    public int SourcePlayerID { get; }

    public PacketBeTeleportedRequest(int sourcePlayerID)
    {
        SourcePlayerID = sourcePlayerID;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(RequestID);
        writer.Write7BitEncodedInt(SourcePlayerID);
    }

    public static PacketBeTeleportedRequest Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.Read7BitEncodedInt();
        return new(reader.Read7BitEncodedInt()) { RequestID = reqID };
    }
}

// client to server
public sealed class PacketBeTeleportedResponse :
    PacketResponse,
    IContextlessPacket<PacketBeTeleportedResponse>
{
    // need we have a deny reason...?

    [MemberNotNullWhen(true, nameof(Session))]
    public bool Accepted => Session is not null;

    public PlayerSessionData? Session { get; }

    public PacketBeTeleportedResponse(PlayerSessionData? session)
    {
        Session = session;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(RequestID);
        if (Accepted)
        {
            writer.Write(true);
            writer.Write(Session);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static PacketBeTeleportedResponse Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.Read7BitEncodedInt();
        bool accept = reader.ReadBoolean();
        return new(accept ? reader.Read<PlayerSessionData>() : null) { RequestID = reqID };
    }
}
