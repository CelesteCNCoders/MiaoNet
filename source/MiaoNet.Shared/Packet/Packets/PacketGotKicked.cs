namespace MiaoNet.Shared;

public enum KickedReason : byte
{
    Manually,
    InvalidPacket,
    InvalidPacketWithState
}

public sealed class PacketGotKicked : IContextlessPacket<PacketGotKicked>
{
    public static PacketGotKicked Deserialize(ref RefBinaryReader reader)
    {
        throw new NotImplementedException();
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        throw new NotImplementedException();
    }
}