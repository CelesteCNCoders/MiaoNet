namespace MiaoNet.Shared;

public abstract class PacketPlayerNotification
{
    public int PlayerID { get; }

    public PacketPlayerNotification(int playerID)
        => PlayerID = playerID;
}

public sealed class PacketPlayerNotification<TPacket> : IContextlessPacket<PacketPlayerNotification<TPacket>>
    where TPacket : IContextlessPacket<TPacket>
{
    public int PlayerID { get; }

    public TPacket Packet { get; }

    public PacketPlayerNotification(int playerID, TPacket packet)
        => (PlayerID, Packet) = (playerID, packet);

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
        writer.Write(Packet);
    }

    public static PacketPlayerNotification<TPacket> Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<TPacket>());
}

public sealed class PacketContextualPlayerNotification<TPacket>
    : IContextualPacket<PacketContextualPlayerNotification<TPacket>>
    where TPacket : IContextualPacket<TPacket>
{
    public int PlayerID { get; }

    public TPacket Packet { get; }

    public PacketContextualPlayerNotification(int playerID, TPacket packet)
        => (PlayerID, Packet) = (playerID, packet);

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(Packet, context);
    }

    public static PacketContextualPlayerNotification<TPacket> Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    ) => new(reader.ReadInt32(), reader.Read<TPacket,IPacketSerializationContext>(context));
}