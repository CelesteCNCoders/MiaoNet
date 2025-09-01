namespace MiaoNet.Shared;

public sealed class ChannelPlayerStateInfo : IRefBinarySerializable<ChannelPlayerStateInfo>
{
    public int ChannelID { get; }

    public PlayerInfo Info { get; }

    public PlayerStateInfo StateInfo { get; }

    public ChannelPlayerStateInfo(int channelID, PlayerInfo info, PlayerStateInfo stateInfo)
    {
        ChannelID = channelID;
        Info = info;
        StateInfo = stateInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(Info);
        writer.Write(StateInfo);
    }

    public static ChannelPlayerStateInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PlayerInfo>(), reader.Read<PlayerStateInfo>());
}