namespace MiaoNet.Shared;

public abstract class PacketResponse
{
    public int ID { get; set; }

    public PacketResponse(int id)
    {
        ID = id;
    }
}