namespace MiaoNet.Shared;

public sealed class PacketPlayerChannelMovedResponse : IContextualPacket<PacketPlayerChannelMovedResponse>
{
    public int ChannelID { get; }

    public IReadOnlyCollection<PlayerMovedInitialData>? Players { get; }

    public PacketPlayerChannelMovedResponse(int channelID, IReadOnlyCollection<PlayerMovedInitialData>? players)
    {
        ChannelID = channelID;
        Players = players;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(ChannelID);
        if (Players is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(Players, context.PooledStringManager);
        }
    }

    public static PacketPlayerChannelMovedResponse Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        return new(
            reader.ReadInt32(),
            reader.ReadBoolean() 
                ? reader.ReadArray<PlayerMovedInitialData, PooledStringManager>(context.PooledStringManager)
                : null
        );
    }
}