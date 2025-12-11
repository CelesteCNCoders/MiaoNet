namespace MiaoNet.Shared;

public struct ChannelInfo : IRefBinarySerializable<ChannelInfo>
{
    public int ID { get; }

    public string Name { get; set; }

    public ChannelInfo(int id, string name)
    {
        Name = name;
        ID = id;
    }

    public readonly override string ToString()
        => $"C{ID}-{Name}";

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ID);
        writer.Write(Name);
    }

    public static ChannelInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadString());
}