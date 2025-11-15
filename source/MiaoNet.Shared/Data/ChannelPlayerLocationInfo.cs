using System.Diagnostics;

namespace MiaoNet.Shared;

[DebuggerDisplay("{Info} at {LocationInfo} in C{ChannelID}")]
public sealed class ChannelPlayerLocationInfo : IRefBinarySerializable<ChannelPlayerLocationInfo>
{
    public int ChannelID { get; }

    public PlayerInfo Info { get; }

    public PlayerLocation LocationInfo { get; }

    public ChannelPlayerLocationInfo(int channelID, PlayerInfo info, PlayerLocation locationInfo)
    {
        ChannelID = channelID;
        Info = info;
        LocationInfo = locationInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ChannelID);
        writer.Write(Info);
        writer.Write(LocationInfo);
    }

    public static ChannelPlayerLocationInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PlayerInfo>(), reader.Read<PlayerLocation>());
}