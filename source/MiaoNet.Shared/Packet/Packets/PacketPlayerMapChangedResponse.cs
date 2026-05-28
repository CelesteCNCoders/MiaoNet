namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChangedResponse : IContextualPacket<PacketPlayerMapChangedResponse>
{
    public IReadOnlyCollection<PlayerMovedInitialData> Players { get; }

    public PacketPlayerMapChangedResponse(IReadOnlyCollection<PlayerMovedInitialData> players)
    {
        Players = players;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
        => writer.Write(Players, context.PooledStringManager);

    public static PacketPlayerMapChangedResponse Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        return new(reader.ReadArray<PlayerMovedInitialData, PooledStringManager>(context.PooledStringManager));
    }
}