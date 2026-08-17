namespace MiaoNet.Shared;

public struct ChannelInfo : IRefBinarySerializable<ChannelInfo>
{
    public const int PrivateChannelVirtualID = -1;

    public const int MainChannelID = 0;

    public string Name { get; set; }

    public readonly bool IsPrivate => Name.StartsWith('!');

    // Color?

    public ChannelInfo(string name) 
        => Name = name;

    public readonly void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Name);

    public static ChannelInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
}