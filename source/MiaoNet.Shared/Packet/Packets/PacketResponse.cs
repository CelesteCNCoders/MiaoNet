namespace MiaoNet.Shared;

public abstract class PacketResponse : IContextlessPacket
{
    public int RequestID { get; set; }

    public PacketResponse(int requestID)
    {
        RequestID = requestID;
    }

    public abstract void Serialize(ref RefBinaryWriter writer);
}