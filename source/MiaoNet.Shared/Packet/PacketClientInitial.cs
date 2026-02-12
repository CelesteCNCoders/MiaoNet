namespace MiaoNet.Shared;

public sealed class PacketClientInitial : IContextlessPacket<PacketClientInitial>
{
    public readonly struct Player : IRefBinarySerializable<Player>
    {
        public int ChannelID { get; }

        public int PlayerID { get; }

        public PlayerInfo PlayerInfo { get; }

        public PlayerLocation Location { get; }

        public PlayerGlobalFlags GlobalFlags { get; }

        public Player(
            int channelID, int playerID,
            PlayerInfo playerInfo, PlayerLocation location,
            PlayerGlobalFlags globalFlags
        )
        {
            PlayerID = playerID;
            ChannelID = channelID;
            PlayerInfo = playerInfo;
            Location = location;
            GlobalFlags = globalFlags;
        }

        public static Player Deserialize(ref RefBinaryReader reader)
        {
            return new(
                reader.ReadInt32(), reader.ReadInt32(),
                reader.Read<PlayerInfo>(), reader.Read<PlayerLocation>(),
                (PlayerGlobalFlags)reader.ReadUInt16()
            );
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(ChannelID);
            writer.Write(PlayerID);
            writer.Write(PlayerInfo);
            writer.Write(Location);
            writer.Write((ushort)GlobalFlags);
        }
    }

    public int ChannelID { get; }

    public int PlayerID { get; }

    public PlayerInfo SelfPlayerInfo { get; }

    public IReadOnlyCollection<ChannelInfo> Channels { get; }

    public IReadOnlyCollection<Player> Players { get; }

    public PacketClientInitial(
        int channelID, int playerID,
        PlayerInfo selfPlayerInfo,
        IReadOnlyCollection<ChannelInfo> channels,
        IReadOnlyCollection<Player> players
    )
    {
        ChannelID = channelID;
        PlayerID = playerID;
        SelfPlayerInfo = selfPlayerInfo;
        Channels = channels;
        Players = players;
    }

    static PacketClientInitial IRefBinarySerializable<PacketClientInitial>.Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadInt32(), reader.ReadInt32(),
            reader.Read<PlayerInfo>(),
            reader.ReadArray<ChannelInfo>(),
            reader.ReadArray<Player>()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(PlayerID);
        writer.Write(SelfPlayerInfo);
        writer.Write(Channels);
        writer.Write(Players);
    }
}