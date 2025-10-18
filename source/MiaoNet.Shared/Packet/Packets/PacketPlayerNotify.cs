namespace MiaoNet.Shared;

public abstract class PacketPlayerNotification
{
    public int PlayerID { get; }

    public PacketPlayerNotification(int playerID)
        => PlayerID = playerID;

    public virtual void Serialize(ref RefBinaryWriter writer)
        => writer.Write(PlayerID);
}

public abstract class PacketPlayerNotification<TPacket> where TPacket : IPacket<TPacket>
{
    public int PlayerID { get; }

    public TPacket Packet { get; }

    public PacketPlayerNotification(int playerID, TPacket packet)
        => (PlayerID, Packet) = (playerID, packet);

    public virtual void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
        writer.Write(Packet);
    }
}