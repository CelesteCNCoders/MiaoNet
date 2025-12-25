namespace MiaoNet.Shared;

public abstract class PacketRequest<TResponse> : IContextlessPacket
    where TResponse : PacketResponse
{
    public int RequestID { get; set; }

    public abstract void Serialize(ref RefBinaryWriter writer);
}