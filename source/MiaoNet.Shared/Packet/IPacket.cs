namespace MiaoNet.Shared;

public interface IPacket : IRefBinarySerializable
{
    public static virtual PacketFlags PacketFlags => PacketFlags.None;
}

public interface IPacket<out T> : IPacket, IRefBinarySerializable<T> where T : IPacket<T>
{
}