namespace MiaoNet.Shared;

public readonly struct NetModInfo : IEquatable<NetModInfo>, IRefBinarySerializable<NetModInfo>
{
    public const int MaxNetMods = byte.MaxValue;

    public string Name { get; }

    public ushort ProtocolVersion { get; }

    public NetModInfo(string name, ushort protocolVersion)
    {
        Name = name;
        ProtocolVersion = protocolVersion;
    }

    public bool Equals(NetModInfo other)
        => ProtocolVersion == other.ProtocolVersion
            && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is NetModInfo other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Name, ProtocolVersion);

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(ProtocolVersion);
    }

    public static NetModInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), reader.ReadUInt16());

    public static bool operator ==(NetModInfo left, NetModInfo right)
        => left.Equals(right);

    public static bool operator !=(NetModInfo left, NetModInfo right)
        => !(left == right);
}
