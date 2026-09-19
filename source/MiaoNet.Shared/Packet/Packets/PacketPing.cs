namespace MiaoNet.Shared;

// server to client
// the correlation id travels in PacketEnvelope.RequestID, so this packet has no payload
public sealed class PacketPing : IPacketRequest<PacketPong>, IContextlessPacket<PacketPing>
{
    public void Serialize(ref RefBinaryWriter writer)
    {
    }

    public static PacketPing Deserialize(ref RefBinaryReader reader)
        => new();
}

// client to server
public sealed class PacketPong : IPacketResponse, IContextlessPacket<PacketPong>
{
    public void Serialize(ref RefBinaryWriter writer)
    {
    }

    public static PacketPong Deserialize(ref RefBinaryReader reader)
        => new();
}
