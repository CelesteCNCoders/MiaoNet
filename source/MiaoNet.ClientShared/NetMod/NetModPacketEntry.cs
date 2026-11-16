namespace MiaoNet.Shared;

public sealed class NetModPacketEntry
{
    internal PacketScope Scope { get; }

    internal Type PacketType { get; }

    internal RefBinaryPacketReadHandler Reader { get; }

    internal Action<PacketEnvelope, IContextualPacket> Invoke { get; }

    private NetModPacketEntry(
        PacketScope scope,
        Type packetType,
        RefBinaryPacketReadHandler reader,
        Action<PacketEnvelope, IContextualPacket> invoke
    )
    {
        Scope = scope;
        PacketType = packetType;
        Reader = reader;
        Invoke = invoke;
    }

    public static NetModPacketEntry Create<TPacket>(PacketScope scope, PacketHandler<TPacket> handler)
        where TPacket : IContextualPacket<TPacket>
    {
        ArgumentNullException.ThrowIfNull(handler);

        return new NetModPacketEntry(
            scope,
            typeof(TPacket),
            static (ref r, c) => TPacket.Deserialize(ref r, c),
            (envelope, packet) => handler(envelope, (TPacket)packet)
        );
    }
}
