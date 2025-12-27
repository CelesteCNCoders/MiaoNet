namespace MiaoNet.Shared;

// TODO is it ok to integrate request/response packet into packet system itself
// instead of manually serialize/deserialize request id and response id?
public abstract class PacketRequest<TResponse> : IContextlessPacket
    where TResponse : PacketResponse
{
    public int RequestID { get; set; }

    public abstract void Serialize(ref RefBinaryWriter writer);
}