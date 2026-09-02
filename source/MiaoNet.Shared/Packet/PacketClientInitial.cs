namespace MiaoNet.Shared;

public sealed class PacketClientInitial : IContextlessPacket<PacketClientInitial>
{
    public readonly struct Player : IRefBinarySerializable<Player>
    {
        public int ChannelID { get; }

        public int PlayerID { get; }

        public PlayerInfo PlayerInfo { get; }

        public PlayerLocation Location { get; }

        public uint PlayerEpoch { get; }

        public uint PlayerSequence { get; }

        public PlayerGlobalFlags GlobalFlags { get; }

        public Player(
            int channelID, int playerID,
            PlayerInfo playerInfo, PlayerLocation location,
            uint playerEpoch, uint playerSequence,
            PlayerGlobalFlags globalFlags
        )
        {
            ChannelID = channelID;
            PlayerID = playerID;
            PlayerInfo = playerInfo;
            Location = location;
            PlayerEpoch = playerEpoch;
            PlayerSequence = playerSequence;
            GlobalFlags = globalFlags;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(ChannelID);
            writer.Write(PlayerID);
            writer.Write(PlayerInfo);
            writer.Write(Location);
            writer.Write(PlayerEpoch);
            writer.Write(PlayerSequence);
            writer.Write((ushort)GlobalFlags);
        }

        public static Player Deserialize(ref RefBinaryReader reader)
        {
            return new(
                reader.ReadInt32(), reader.ReadInt32(),
                reader.Read<PlayerInfo>(), reader.Read<PlayerLocation>(),
                reader.ReadUInt32(), reader.ReadUInt32(),
                (PlayerGlobalFlags)reader.ReadUInt16()
            );
        }
    }

    public readonly struct Channel : IRefBinarySerializable<Channel>
    {
        public int ID { get; }

        public ChannelInfo ChannelInfo { get; }

        public Channel(int id, ChannelInfo channelInfo)
        {
            ID = id;
            ChannelInfo = channelInfo;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(ID);
            writer.Write(ChannelInfo);
        }

        public static Channel Deserialize(ref RefBinaryReader reader)
            => new(reader.ReadInt32(), reader.Read<ChannelInfo>());
    }

    public int ChannelID { get; }

    public int PlayerID { get; }

    public PlayerInfo SelfPlayerInfo { get; }

    public IReadOnlyCollection<Channel> Channels { get; }
    
    public IReadOnlyCollection<Player> Players { get; }

    public PlayerPresenceMessage PlayerPresenceMessage { get; }

    public string JoinMessage { get; }

    public ServerFeatureFlags ServerFeatures { get; }

    public PacketClientInitial(
        int channelID, int playerID,
        PlayerInfo selfPlayerInfo,
        IReadOnlyCollection<Channel> channels,
        IReadOnlyCollection<Player> players,
        PlayerPresenceMessage playerPresenceMessage,
        string joinMessage,
        ServerFeatureFlags serverFeatures = ServerFeatureFlags.None
    )
    {
        ChannelID = channelID;
        PlayerID = playerID;
        SelfPlayerInfo = selfPlayerInfo;
        Channels = channels;
        Players = players;
        PlayerPresenceMessage = playerPresenceMessage;
        JoinMessage = joinMessage;
        ServerFeatures = serverFeatures;
    }

    public static PacketClientInitial Deserialize(ref RefBinaryReader reader)
    {
        int channelID = reader.ReadInt32();
        int playerID = reader.ReadInt32();
        PlayerInfo selfPlayerInfo = reader.Read<PlayerInfo>();
        Channel[] channels = reader.ReadArray<Channel>();
        Player[] players = reader.ReadArray<Player>();
        PlayerPresenceMessage playerPresenceMessage = reader.Read<PlayerPresenceMessage>();
        string joinMessage = reader.ReadString();
        ServerFeatureFlags serverFeatures = reader.BytesLeft >= sizeof(ushort)
            ? (ServerFeatureFlags)reader.ReadUInt16()
            : ServerFeatureFlags.None;
        return new(
            channelID,
            playerID,
            selfPlayerInfo,
            channels,
            players,
            playerPresenceMessage,
            joinMessage,
            serverFeatures
        );
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(PlayerID);
        writer.Write(SelfPlayerInfo);
        writer.Write(Channels);
        writer.Write(Players);
        writer.Write(PlayerPresenceMessage);
        writer.Write(JoinMessage);
        writer.Write((ushort)ServerFeatures);
    }
}
