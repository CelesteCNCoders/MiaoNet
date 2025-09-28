namespace MiaoNet.Shared;

public abstract class PacketRequest
{
    public int ID { get; set; }

    public PacketRequest(int id)
    {
        ID = id;
    }
}