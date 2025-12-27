namespace MiaoNet.Shared;

public abstract class PacketResponse : IContextlessPacket
{
    public int RequestID { get; set; }

    public abstract void Serialize(ref RefBinaryWriter writer);
}