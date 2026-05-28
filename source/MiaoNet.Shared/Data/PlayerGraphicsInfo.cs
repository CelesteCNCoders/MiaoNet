namespace MiaoNet.Shared;

// TODO this is not actually sent currently
// it should be used when it comes to SkinSyncing
public sealed class PlayerGraphicsInfo : IRefBinarySerializable<PlayerGraphicsInfo>, ICloneable
{
    public readonly struct HairInfo : IRefBinarySerializable<HairInfo>
    {
        public byte Length { get; }

        public Color Color { get; }

        public HairInfo(byte length, Color color)
        {
            Length = length;
            Color = color;
        }

        public static HairInfo Deserialize(ref RefBinaryReader reader)
            => new(reader.ReadByte(), reader.ReadColor());

        public readonly void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(Length);
            writer.Write(Color);
        }
    }

    // TODO other modes?
    // eg. real time update mode(like CelesteNet)
    //     extended dashes
    //     hair color per segment
    public HairInfo Dash0HairInfo;
    public HairInfo Dash1HairInfo;
    public HairInfo Dash2HairInfo;
    public HairInfo FeatherHairInfo;

    public readonly static PlayerGraphicsInfo Default;

    static PlayerGraphicsInfo()
    {
        Color dash0HairColor = new Color(0x44, 0xB7, 0xFF);
        Color dash1HairColor = new Color(0xAC, 0x32, 0x32);
        Color dash2HairColor = new Color(0xFF, 0x6D, 0xEF);
        Color featherHairColor = new Color(0xF2, 0xEB, 0x6D);
        Default = new(new(4, dash0HairColor), new(4, dash1HairColor), new(5, dash2HairColor), new(7, featherHairColor));
    }

    public PlayerGraphicsInfo(
        HairInfo dash0HairInfo,
        HairInfo dash1HairInfo,
        HairInfo dash2HairInfo,
        HairInfo featherHairInfo
    )
    {
        Dash0HairInfo = dash0HairInfo;
        Dash1HairInfo = dash1HairInfo;
        Dash2HairInfo = dash2HairInfo;
        FeatherHairInfo = featherHairInfo;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Dash0HairInfo);
        writer.Write(Dash1HairInfo);
        writer.Write(Dash2HairInfo);
        writer.Write(FeatherHairInfo);
    }

    public static PlayerGraphicsInfo Deserialize(ref RefBinaryReader reader)
        => new(
            dash0HairInfo: reader.Read<HairInfo>(),
            dash1HairInfo: reader.Read<HairInfo>(),
            dash2HairInfo: reader.Read<HairInfo>(),
            featherHairInfo: reader.Read<HairInfo>()
        );

    public HairInfo GetHairInfo(int dashes) => dashes switch
    {
        <= 0 => Dash0HairInfo,
        1 => Dash1HairInfo,
        2 => Dash2HairInfo,
        > 2 => Dash2HairInfo
    };

    public PlayerGraphicsInfo Clone() 
        => new(Dash0HairInfo, Dash1HairInfo, Dash2HairInfo, FeatherHairInfo);

    object ICloneable.Clone()
        => Clone();
}