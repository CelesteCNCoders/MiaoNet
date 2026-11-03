namespace MiaoNet.Shared;

// client -> server
internal sealed class PacketNetMod : IContextualPacket<PacketNetMod>
{
    public byte NetModID { get; }

    public byte LocalID { get; }

    public PacketRoute Route { get; }

    public bool CanBatch => true;

    private readonly IContextualPacket? packet;

    private readonly ReadOnlyMemory<byte> payload;

    public ReadOnlyMemory<byte> Payload => payload;

    private PacketNetMod(
        byte netModID,
        byte localID,
        PacketRoute route,
        IContextualPacket? packet,
        ReadOnlyMemory<byte> payload
    )
    {
        NetModID = netModID;
        LocalID = localID;
        Route = route;
        this.packet = packet;
        this.payload = payload;
    }

    public static PacketNetMod FromPacket(
        byte netModID,
        byte localID,
        PacketRoute route,
        IContextualPacket packet
    ) => new(netModID, localID, route, packet, default);

    public static PacketNetMod FromPayload(
        byte netModID,
        byte localID,
        PacketRoute route,
        ReadOnlyMemory<byte> payload
    ) => new(netModID, localID, route, null, payload);

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(NetModID);
        writer.Write(LocalID);
        Route.Write(ref writer);

        if (packet is null)
            writer.WriteSpan(payload.Span);
        else
            packet.Serialize(ref writer, context);
    }

    public static PacketNetMod Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        byte netModID = reader.ReadByte();
        byte localID = reader.ReadByte();
        PacketRoute route = PacketRoute.Read(ref reader);

        return FromPayload(netModID, localID, route, reader.ReadSpan(reader.BytesLeft).ToArray());
    }
}
