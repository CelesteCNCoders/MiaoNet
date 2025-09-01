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

public sealed class PacketPlayerChannelMoveNotify : PacketPlayerNotify<PacketPlayerChannelMove>, IPacket<PacketPlayerChannelMoveNotify>
{
    public PacketPlayerChannelMoveNotify(int playerID, PacketPlayerChannelMove packet)
        : base(playerID, packet)
    {
    }

    public static PacketPlayerChannelMoveNotify Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PacketPlayerChannelMove>());
}