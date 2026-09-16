namespace MiaoNet.Shared;

public sealed class PacketUpdateGlobalFlag : IContextlessPacket<PacketUpdateGlobalFlag>
{
    public PlayerGlobalFlags Flags { get; set; }

    public PacketUpdateGlobalFlag(PlayerGlobalFlags flag)
    {
        Flags = flag;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Flags);
    }

    public static PacketUpdateGlobalFlag Deserialize(ref RefBinaryReader reader)
    {
        return new((PlayerGlobalFlags)reader.ReadByte());
    }
}
