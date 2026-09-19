namespace MiaoNet.Shared;

public sealed class PacketPlayerLeft : IContextlessPacket<PacketPlayerLeft>
{
    public enum LeftReason
    {
        Manually,
        Inactive,
        Interrupted
    }

    public int PlayerID { get; }

    public PacketPlayerLeft(int playerID)
    {
        PlayerID = playerID;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write7BitEncodedInt(PlayerID);

    public static PacketPlayerLeft Deserialize(ref RefBinaryReader reader)
        => new(reader.Read7BitEncodedInt());
}
