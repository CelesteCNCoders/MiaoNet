namespace MiaoNet.Shared;

public sealed class PacketClientInitial : IPacket, IRefBinarySerializable<PacketClientInitial>
{
    public int ChannelID { get; }

    public PlayerInfo SelfPlayerInfo { get; }

    public IReadOnlyList<ChannelStateInfo> Channels { get; }

    public IReadOnlyList<ChannelPlayerStateInfo> Players { get; }

    public PacketClientInitial(
        PlayerInfo selfPlayerInfo,
        IReadOnlyList<ChannelStateInfo> channels,
        IReadOnlyList<ChannelPlayerStateInfo> players
    )
    {
        SelfPlayerInfo = selfPlayerInfo;
        Channels = channels;
        Players = players;
    }

    static PacketClientInitial IRefBinarySerializable<PacketClientInitial>.Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<PlayerInfo>(), reader.ReadList<ChannelStateInfo>(), reader.ReadList<ChannelPlayerStateInfo>());

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SelfPlayerInfo);
        writer.Write(Channels);
        writer.Write(Players);
    }
}