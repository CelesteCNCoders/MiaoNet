namespace MiaoNet.Shared;

public sealed class PacketPlayerJoined : IPacket<PacketPlayerJoined>
{
    public int ChannelID { get; }

    public PlayerInfo PlayerInfo { get; }

    public PlayerOnlineStatus OnlineStatus { get; }

    public PacketPlayerJoined(int channelID, PlayerInfo playerInfo)
    {
        ChannelID = channelID;
        PlayerInfo = playerInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(PlayerInfo);
    }

    public static PacketPlayerJoined Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PlayerInfo>());
}