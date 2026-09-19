using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// server to client
public sealed class PacketBeTeleportedRequest :
    IPacketRequest<PacketBeTeleportedResponse>,
    IContextlessPacket<PacketBeTeleportedRequest>
{
    public int SourcePlayerID { get; }

    public PacketBeTeleportedRequest(int sourcePlayerID)
    {
        SourcePlayerID = sourcePlayerID;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(SourcePlayerID);
    }

    public static PacketBeTeleportedRequest Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.Read7BitEncodedInt());
    }
}

// client to server
public sealed class PacketBeTeleportedResponse :
    IPacketResponse,
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

    public void Serialize(ref RefBinaryWriter writer)
    {
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
        bool accept = reader.ReadBoolean();
        return new(accept ? reader.Read<PlayerSessionData>() : null);
    }
}
