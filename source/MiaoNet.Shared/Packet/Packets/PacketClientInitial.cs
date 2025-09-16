namespace MiaoNet.Shared;

public sealed class PacketClientInitial : IPacket<PacketClientInitial>
{
    public int ChannelID { get; }

    public PlayerInfo SelfPlayerInfo { get; }

    public IReadOnlyList<ChannelStateInfo> Channels { get; }

    public IReadOnlyList<PacketPlayerJoined> Players { get; }

    public PacketClientInitial(
        PlayerInfo selfPlayerInfo,
        IReadOnlyList<ChannelStateInfo> channels,
        IReadOnlyList<PacketPlayerJoined> players
    )
    {
        SelfPlayerInfo = selfPlayerInfo;
        Channels = channels;
        Players = players;
    }

    static PacketClientInitial IRefBinarySerializable<PacketClientInitial>.Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerInfo>(),
            reader.ReadList<ChannelStateInfo>(),
            reader.ReadList<PacketPlayerJoined>()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SelfPlayerInfo);
        writer.Write(Channels);
        writer.Write(Players);
    }
}