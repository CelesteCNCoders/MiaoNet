namespace MiaoNet.Shared;

[Flags]
public enum ChatChannelFlags : byte
{
    None = 0,
    CanSend = 1 << 0,
    DefaultVisibleInGlobal = 1 << 1
}

public readonly struct ChatChannelInfo : IRefBinarySerializable<ChatChannelInfo>
{
    public int ID { get; }

    public string Key { get; }

    public string DisplayName { get; }

    public string Prefix { get; }

    public Color Color { get; }

    public ChatChannelFlags Flags { get; }

    public bool CanSend => Flags.HasFlag(ChatChannelFlags.CanSend);

    public bool DefaultVisibleInGlobal => Flags.HasFlag(ChatChannelFlags.DefaultVisibleInGlobal);

    public ChatChannelInfo(
        int id,
        string key,
        string displayName,
        string prefix,
        Color color,
        ChatChannelFlags flags
    )
    {
        ID = id;
        Key = key;
        DisplayName = displayName;
        Prefix = prefix;
        Color = color;
        Flags = flags;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(ID);
        writer.Write(Key);
        writer.Write(DisplayName);
        writer.Write(Prefix);
        writer.Write(Color);
        writer.Write((byte)Flags);
    }

    public static ChatChannelInfo Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read7BitEncodedInt(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadColor(),
            (ChatChannelFlags)reader.ReadByte()
        );
}
