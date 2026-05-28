namespace MiaoNet.Shared;

public sealed class PacketChannelCreateAndJoin : IContextlessPacket<PacketChannelCreateAndJoin>
{
    public ChannelInfo ChannelInfo { get; }

    public PacketChannelCreateAndJoin(ChannelInfo channelInfo)
    {
        ChannelInfo = channelInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelInfo);
    }

    public static PacketChannelCreateAndJoin Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.Read<ChannelInfo>());
    }
}