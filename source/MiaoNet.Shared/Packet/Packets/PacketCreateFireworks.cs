namespace MiaoNet.Shared;

public sealed class PacketCreateFireworks : IContextlessPacket<PacketCreateFireworks>
{
    public Color Color { get; }

    public float InitialSpeed { get; }

    public PacketCreateFireworks(Color color, float initialSpeed)
    {
        Color = color;
        InitialSpeed = initialSpeed;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Color);
        writer.Write(InitialSpeed);
    }

    public static PacketCreateFireworks Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadColor(), reader.ReadSingle());
    }
}