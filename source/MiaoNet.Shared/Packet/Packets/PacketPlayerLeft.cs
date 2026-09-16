namespace MiaoNet.Shared;

public sealed class PacketPlayerLeft : PacketPlayerNotification, IContextlessPacket<PacketPlayerLeft>
{
    public enum LeftReason
    {
        Manually,
        Inactive,
        Interrupted
    }

    public LeftReason Reason { get; set; }

    public PacketPlayerLeft(int playerID)
        : base(playerID)
    {
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write7BitEncodedInt(PlayerID);

    public static PacketPlayerLeft Deserialize(ref RefBinaryReader reader)
        => new(reader.Read7BitEncodedInt());
}
