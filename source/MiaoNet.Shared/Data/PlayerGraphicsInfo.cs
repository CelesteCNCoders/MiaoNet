namespace MiaoNet.Shared;

public sealed class PlayerGraphicsInfo : IRefBinarySerializable<PlayerGraphicsInfo>
{
    // hm?
    public byte Dash0HairLength { get; set; }
    public byte Dash1HairLength { get; set; }
    public byte Dash2HairLength { get; set; }
    public Color Dash0Color { get; set; }
    public Color Dash1Color { get; set; }
    public Color Dash2Color { get; set; }
    public Color FeatherColor { get; set; }

    public readonly static PlayerGraphicsInfo Default;

    static PlayerGraphicsInfo()
    {
        Color usedHairColor = new Color(0x44, 0xB7, 0xFF);
        Color normalHairColor = new Color(0xAC, 0x32, 0x32);
        Color featherColor = new Color(0xF2, 0xEB, 0x6D);
        Color twoDashesHairColor = new Color(0xFF, 0x6D, 0xEF);
        Default = new(4, 4, 5, usedHairColor, normalHairColor, twoDashesHairColor, featherColor);
    }

    public PlayerGraphicsInfo(
        byte dash0HairLength, byte dash1HairLength, byte dash2HairLength,
        Color dash0Color, Color dash1Color, Color dash2Color, Color featherColor
    )
    {
        Dash0HairLength = dash0HairLength;
        Dash1HairLength = dash1HairLength;
        Dash2HairLength = dash2HairLength;
        Dash0Color = dash0Color;
        Dash1Color = dash1Color;
        Dash2Color = dash2Color;
        FeatherColor = featherColor;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Dash0HairLength);
        writer.Write(Dash0Color);
        writer.Write(Dash1Color);
        writer.Write(Dash2Color);
    }

    public static PlayerGraphicsInfo Deserialize(ref RefBinaryReader reader)
        => new(
                dash0HairLength: reader.ReadByte(),
                dash1HairLength: reader.ReadByte(),
                dash2HairLength: reader.ReadByte(),
                dash0Color: reader.ReadColor(),
                dash1Color: reader.ReadColor(),
                dash2Color: reader.ReadColor(),
                featherColor: reader.ReadColor()
            );
}