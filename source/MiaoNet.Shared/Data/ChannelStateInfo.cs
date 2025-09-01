namespace MiaoNet.Shared;

public sealed class ChannelStateInfo : IRefBinarySerializable<ChannelStateInfo>
{
    public int ID { get; }

    public string Name { get; }

    public ChannelStateInfo(int id, string name)
    {
        Name = name;
        ID = id;
    }

    public override string ToString()
        => $"C{ID}-{Name}";

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ID);
        writer.Write(Name);
    }

    public static ChannelStateInfo Deserialize(ref RefBinaryReader reader) 
        => new(reader.ReadInt32(), reader.ReadString());
}