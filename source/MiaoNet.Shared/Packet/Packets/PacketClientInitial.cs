namespace MiaoNet.Shared;

public sealed class PacketClientInitial : IPacket<PacketClientInitial>
{
    public readonly struct Player : IRefBinarySerializable<Player>
    {
        public int ChannelID { get; }

        public PlayerInfo PlayerInfo { get; }

        public PlayerLocation Location { get; }

        public Player(
            int channelID, PlayerInfo playerInfo, PlayerLocation location
        )
        {
            ChannelID = channelID;
            PlayerInfo = playerInfo;
            Location = location;
        }

        public static Player Deserialize(ref RefBinaryReader reader)
        {
            return new(reader.ReadInt32(), reader.Read<PlayerInfo>(), reader.Read<PlayerLocation>());
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(ChannelID);
            writer.Write(PlayerInfo);
            writer.Write(Location);
        }
    }

    public int ChannelID { get; }

    public PlayerInfo SelfPlayerInfo { get; }

    public IReadOnlyCollection<ChannelInfo> Channels { get; }

    public IReadOnlyCollection<Player> Players { get; }

    public PacketClientInitial(
        int channelID,
        PlayerInfo selfPlayerInfo,
        IReadOnlyCollection<ChannelInfo> channels,
        IReadOnlyCollection<Player> players
    )
    {
        ChannelID = channelID;
        SelfPlayerInfo = selfPlayerInfo;
        Channels = channels;
        Players = players;
    }

    static PacketClientInitial IRefBinarySerializable<PacketClientInitial>.Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadInt32(),
            reader.Read<PlayerInfo>(),
            reader.ReadArray<ChannelInfo>(),
            reader.ReadArray<Player>()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(SelfPlayerInfo);
        writer.Write(Channels);
        writer.Write(Players);
    }
}