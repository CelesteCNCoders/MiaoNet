namespace MiaoNet.Shared;

public sealed class PacketPlayerChannelMove : IPacket<PacketPlayerChannelMove>
{
    public int ChannelID { get; }

    public PacketPlayerChannelMove(int channelID)
    {
        ChannelID = channelID;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(ChannelID);

    public static PacketPlayerChannelMove Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32());
}