namespace MiaoNet.Shared;

public sealed class PacketChannelCreated : IContextlessPacket<PacketChannelCreated>
{
    public int ChannelID { get; }

    public ChannelInfo ChannelInfo { get; }

    public PacketChannelCreated(int channelID, ChannelInfo channelInfo)
    {
        ChannelID = channelID;
        ChannelInfo = channelInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(ChannelInfo);
    }

    public static PacketChannelCreated Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadInt32(), reader.Read<ChannelInfo>());
    }
}