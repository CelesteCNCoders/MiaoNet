namespace MiaoNet.Shared;

// server to client
public sealed class PacketPing : PacketRequest<PacketPong>, IContextlessPacket<PacketPing>
{
    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
    }

    public static PacketPing Deserialize(ref RefBinaryReader reader)
    {
        return new() { RequestID = reader.ReadInt32() };
    }
}

// client to server
public sealed class PacketPong : PacketResponse, IContextlessPacket<PacketPong>
{
    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
    }

    public static PacketPong Deserialize(ref RefBinaryReader reader)
    {
        return new() { RequestID = reader.ReadInt32() };
    }
}