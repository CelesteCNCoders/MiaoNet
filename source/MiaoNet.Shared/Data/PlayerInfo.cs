namespace MiaoNet.Shared;

public sealed class PlayerInfo : IRefBinarySerializable<PlayerInfo>
{
    public string Name { get; }

    public string Prefix { get; }

    public string AvatarUrl { get; }

    public Color Color { get; }

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(Prefix))
                return Name;
            else
                return $"[{Prefix}] {Name}";
        }
    }

    public PlayerInfo(string name, string prefix, string avatarUrl, Color color)
    {
        Name = name;
        Prefix = prefix;
        AvatarUrl = avatarUrl;
        Color = color;
    }

    public override string ToString()
        => DisplayName;

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(Prefix);
        writer.Write(AvatarUrl);
        writer.Write(Color);
    }

    public static PlayerInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadColor());
}