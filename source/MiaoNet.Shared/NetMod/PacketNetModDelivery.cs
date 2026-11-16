namespace MiaoNet.Shared;

// server -> client
internal sealed class PacketNetModDelivery : IContextualPacket<PacketNetModDelivery>
{
    public bool CanBatch => true;

    public byte NetModID { get; }

    public byte LocalID { get; }

    private readonly ReadOnlyMemory<byte> payload;

    public ReadOnlyMemory<byte> Payload => payload;

    private PacketNetModDelivery(byte netModID, byte localID, ReadOnlyMemory<byte> payload)
    {
        NetModID = netModID;
        LocalID = localID;
        this.payload = payload;
    }

    public static PacketNetModDelivery From(byte netModID, byte localID, ReadOnlyMemory<byte> payload)
        => new(netModID, localID, payload);

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(NetModID);
        writer.Write(LocalID);
        writer.WriteSpan(payload.Span);
    }

    public static PacketNetModDelivery Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new(
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadSpan(reader.BytesLeft).ToArray()
        );
}
