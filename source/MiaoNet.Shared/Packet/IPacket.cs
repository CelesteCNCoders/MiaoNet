namespace MiaoNet.Shared;

public interface IContextualPacket : IContextualRefBinarySerializable<IPacketSerializationContext>
{
    public static virtual PacketFlags PacketFlags => PacketFlags.None;
}

public interface IContextualPacket<out T> : IContextualPacket, IContextualRefBinarySerializable<T, IPacketSerializationContext>
    where T : IContextualPacket<T>
{
}

public interface IContextlessPacket : IContextualPacket, IRefBinarySerializable
{
    void IContextualRefBinarySerializable<IPacketSerializationContext>.Serialize(
        ref RefBinaryWriter writer,
        IPacketSerializationContext context
    ) => Serialize(ref writer);
}

public interface IContextlessPacket<out T> : IContextlessPacket, IContextualPacket<T>, IRefBinarySerializable<T>
    where T : IContextlessPacket<T>
{
    static T IContextualRefBinarySerializable<T, IPacketSerializationContext>.Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    ) => T.Deserialize(ref reader);
}