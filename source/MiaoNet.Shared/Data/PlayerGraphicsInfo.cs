namespace MiaoNet.Shared;

public sealed class PlayerGraphicsInfo : IRefBinarySerializable<PlayerGraphicsInfo>
{
    public int HairLength;

    // TODO more dashes?
    public Color Dash0Color;

    public Color Dash1Color;

    public Color Dash2Color;

    public PlayerGraphicsInfo(int hairLength, Color dash0Color, Color dash1Color, Color dash2Color)
    {
        HairLength = hairLength;
        Dash0Color = dash0Color;
        Dash1Color = dash1Color;
        Dash2Color = dash2Color;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(HairLength);
        writer.Write(Dash0Color);
        writer.Write(Dash1Color);
        writer.Write(Dash2Color);
    }

    public static PlayerGraphicsInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadColor(), reader.ReadColor(), reader.ReadColor());
}