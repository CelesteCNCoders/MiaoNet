namespace Celeste.Mod.CelesteNet.DataTypes;

public class MetaRef : MetaType<MetaRef>
{
    static MetaRef() => MetaID = "ref";

    public uint ID;
    public bool IsAlive;

    public MetaRef() { }
    public MetaRef(uint id, bool isAlive = true) { ID = id; IsAlive = isAlive; }
    public static implicit operator uint(MetaRef value) => value.ID;

    public override void Read(CelesteNetBinaryReader reader)
    {
        ID = reader.Read7BitEncodedUInt();
        IsAlive = reader.ReadBoolean();
    }

    public override void Write(CelesteNetBinaryWriter writer)
    {
        writer.Write7BitEncodedUInt(ID);
        writer.Write(IsAlive);
    }
}

public class MetaBoundRef : MetaType<MetaBoundRef>
{
    static MetaBoundRef() => MetaID = "bound";

    public string TypeBoundTo = "";
    public uint ID;
    public bool IsAlive;

    public MetaBoundRef() { }

    public MetaBoundRef(string typeBoundTo, uint id, bool isAlive)
    {
        TypeBoundTo = typeBoundTo;
        ID = id;
        IsAlive = isAlive;
    }

    public override void Read(CelesteNetBinaryReader reader)
    {
        TypeBoundTo = reader.ReadNetString();
        ID = reader.Read7BitEncodedUInt();
        IsAlive = reader.ReadBoolean();
    }

    public override void Write(CelesteNetBinaryWriter writer)
    {
        writer.WriteNetString(TypeBoundTo);
        writer.Write7BitEncodedUInt(ID);
        writer.Write(IsAlive);
    }
}
