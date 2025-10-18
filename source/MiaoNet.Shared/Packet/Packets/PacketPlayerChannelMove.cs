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

public sealed class PacketPlayerChannelMoveNotification : PacketPlayerNotification<PacketPlayerChannelMove>, IPacket<PacketPlayerChannelMoveNotification>
{
    public PacketPlayerChannelMoveNotification(int playerID, PacketPlayerChannelMove packet)
        : base(playerID, packet)
    {
    }

    public static PacketPlayerChannelMoveNotification Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PacketPlayerChannelMove>());
}