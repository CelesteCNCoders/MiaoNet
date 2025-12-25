namespace MiaoNet.Shared;

public sealed class PacketUpdateOnlineStatus : IContextlessPacket<PacketUpdateOnlineStatus>
{
    public PlayerOnlineStatus Status { get; set; }

    public PacketUpdateOnlineStatus(PlayerOnlineStatus status)
    {
        Status = status;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Status);
    }

    public static PacketUpdateOnlineStatus Deserialize(ref RefBinaryReader reader)
    {
        return new((PlayerOnlineStatus)reader.ReadByte());
    }
}