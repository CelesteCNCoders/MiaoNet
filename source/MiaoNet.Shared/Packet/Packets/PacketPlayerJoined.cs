namespace MiaoNet.Shared;

public sealed class PacketPlayerJoined : IContextlessPacket<PacketPlayerJoined>
{
    public int ChannelID { get; }

    public int PlayerID { get; }

    public PlayerInfo PlayerInfo { get; }

    public PacketPlayerJoined(int channelID, int playerID, PlayerInfo playerInfo)
    {
        ChannelID = channelID;
        PlayerID = playerID;
        PlayerInfo = playerInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(ChannelID);
        writer.Write7BitEncodedInt(PlayerID);
        writer.Write(PlayerInfo);
    }

    public static PacketPlayerJoined Deserialize(ref RefBinaryReader reader)
        => new(reader.Read7BitEncodedInt(), reader.Read7BitEncodedInt(), reader.Read<PlayerInfo>());
}
