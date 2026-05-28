namespace MiaoNet.Shared;

public struct ChannelInfo : IRefBinarySerializable<ChannelInfo>
{
    public string Name { get; set; }

    // Color?

    public ChannelInfo(string name) 
        => Name = name;

    public readonly void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Name);

    public static ChannelInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
}