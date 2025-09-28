namespace MiaoNet.Shared;

public abstract class PacketPlayerNotify
{
    public int PlayerID { get; }

    public PacketPlayerNotify(int playerID)
        => PlayerID = playerID;

    public virtual void Serialize(ref RefBinaryWriter writer)
        => writer.Write(PlayerID);
}

public abstract class PacketPlayerNotify<TPacket> where TPacket : IPacket<TPacket>
{
    public int PlayerID { get; }

    public TPacket Packet { get; }

    public PacketPlayerNotify(int playerID, TPacket packet)
        => (PlayerID, Packet) = (playerID, packet);

    public virtual void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
        writer.Write(Packet);
    }
}