namespace MiaoNet.Shared;

public sealed class PacketPlayerGraphicsUpdate : IContextlessPacket<PacketPlayerGraphicsUpdate>
{
    public enum UpdateFlags : byte
    {
        HairDash0,
        HairDash1,
        HairDash2,
        HairFeather,
        SpriteMode
    }

    public PacketPlayerGraphicsUpdate()
    {
        
    }

    public static PacketPlayerGraphicsUpdate Deserialize(ref RefBinaryReader reader)
    {
        throw new NotImplementedException();
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        throw new NotImplementedException();
    }
}