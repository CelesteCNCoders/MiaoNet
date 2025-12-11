namespace MiaoNet.Shared;

public sealed class PacketClientInitial : IPacket<PacketClientInitial>
{
    public readonly struct Player : IRefBinarySerializable<Player>
    {
        [Flags]
        public enum DataFlags : byte
        {
            HasGraphicsInfo = 1 << 0,
            HasState = 1 << 1
        }

        public int ChannelID { get; }

        public PlayerInfo PlayerInfo { get; }

        public PlayerLocation Location { get; }

        public PlayerGraphicsInfo? GraphicsInfo { get; }

        public PlayerState? State { get; }

        public Player(
            int channelID, PlayerInfo playerInfo, PlayerLocation location,
            PlayerGraphicsInfo? graphicsInfo,
            PlayerState? state
        )
        {
            ChannelID = channelID;
            PlayerInfo = playerInfo;
            Location = location;
            GraphicsInfo = graphicsInfo;
            State = state;
        }

        public static Player Deserialize(ref RefBinaryReader reader)
        {
            DataFlags flags = (DataFlags)reader.ReadByte();
            PlayerGraphicsInfo? gfxInfo = null;
            PlayerState? state = null;
            if (flags.HasFlag(DataFlags.HasGraphicsInfo))
                gfxInfo = reader.Read<PlayerGraphicsInfo>();
            if (flags.HasFlag(DataFlags.HasState))
                state = reader.Read<PlayerState>();
            return new(reader.ReadInt32(), reader.Read<PlayerInfo>(), reader.Read<PlayerLocation>(), gfxInfo, state);
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            DataFlags flags = 0;
            if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
            if (State is not null) flags |= DataFlags.HasState;
            writer.Write((byte)flags);
            if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
            if (State is not null) writer.Write(State);
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
        PlayerInfo selfPlayerInfo,
        IReadOnlyCollection<ChannelInfo> channels,
        IReadOnlyCollection<Player> players
    )
    {
        SelfPlayerInfo = selfPlayerInfo;
        Channels = channels;
        Players = players;
    }

    static PacketClientInitial IRefBinarySerializable<PacketClientInitial>.Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerInfo>(),
            reader.ReadArray<ChannelInfo>(),
            reader.ReadArray<Player>()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SelfPlayerInfo);
        writer.Write(Channels);
        writer.Write(Players);
    }
}