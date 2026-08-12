namespace MiaoNet.Shared;

public sealed class PacketPlayerFrame : IContextualPacket<PacketPlayerFrame>
{
    public PlayerStateDelta StateDelta { get; }

    public PacketPlayerFrame(PlayerStateDelta stateDelta)
    {
        StateDelta = stateDelta;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(StateDelta, context.PooledStringManager);
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        return new(reader.Read<PlayerStateDelta, PooledStringManager>(context.PooledStringManager));
    }
}