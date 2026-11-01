using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.DataTypes;

public class DataPlayerInfo : DataType<DataPlayerInfo>
{
    static DataPlayerInfo() => DataID = "playerInfo";

    public uint ID;
    public string Name = "";
    public string FullName = "";
    public string AvatarID = "";
    public string AvatarURL = "";
    public string Prefix = "";
    public Color NameColor = Color.White;
    public string DisplayName = "";

    public override MetaType[] GenerateMeta(DataContext context) => [new MetaRef(ID, !string.IsNullOrEmpty(FullName))];
    public override void FixupMeta(DataContext context) => ID = Get<MetaRef>(context).ID;

    protected override void Read(CelesteNetBinaryReader reader)
    {
        Name = reader.ReadNetString();
        FullName = reader.ReadNetString();
        AvatarID = reader.ReadNetString();
        AvatarURL = reader.ReadNetString();
        Prefix = reader.ReadNetString();
        NameColor = reader.ReadColorNoA();
    }

    protected override void Write(CelesteNetBinaryWriter writer)
    {
        writer.WriteNetString(Name);
        writer.WriteNetString(FullName);
        writer.WriteNetString(AvatarID);
        writer.WriteNetString(AvatarURL);
        writer.WriteNetString(Prefix);
        writer.WriteNoA(NameColor);
    }
}
