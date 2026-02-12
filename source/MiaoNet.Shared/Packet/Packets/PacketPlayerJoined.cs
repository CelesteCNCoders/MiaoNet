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
        writer.Write(ChannelID);
        writer.Write(PlayerID);
        writer.Write(PlayerInfo);
    }

    public static PacketPlayerJoined Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(), reader.Read<PlayerInfo>());
}