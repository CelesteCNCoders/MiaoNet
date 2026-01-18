namespace MiaoNet.Shared;

public sealed class PacketPlayerGrabJumpOut : IContextlessPacket<PacketPlayerGrabJumpOut>
{
    public int PlayerID { get; }

    public PacketPlayerGrabJumpOut(int playerID)
    {
        PlayerID = playerID;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
    }

    public static PacketPlayerGrabJumpOut Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadInt32());
    }
}