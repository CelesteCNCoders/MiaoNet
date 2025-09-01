namespace MiaoNet.Shared;

public sealed class PacketPlayerLeft : PacketPlayerNotify, IPacket<PacketPlayerLeft>
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
        => writer.Write(PlayerID);

    public static PacketPlayerLeft Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32());
}