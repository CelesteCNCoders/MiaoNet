namespace MiaoNet.Shared;

public readonly struct PlayerMap : IEquatable<PlayerMap>, IRefBinarySerializable<PlayerMap>
{
    public string Sid { get; }

    public AreaMode AreaMode { get; }

    public readonly char AreaModeCharacter => (char)('A' + (char)AreaMode);

    public bool IsEmpty => Sid.Length == 0;

    public PlayerMap()
    {
        Sid = string.Empty;
        AreaMode = AreaMode.Normal;
    }

    public PlayerMap(string sid, AreaMode areaMode)
    {
        if (sid.Length == 0)
            SafeGuard.Assert(areaMode is AreaMode.Normal);
        Sid = sid;
        AreaMode = areaMode;
    }

    public override string ToString()
        => IsEmpty ? string.Empty : $"{Sid} {AreaModeCharacter}";

    public override bool Equals(object? obj)
        => obj is PlayerMap map && Equals(map);

    public bool Equals(PlayerMap other)
        => Sid == other.Sid && AreaMode == other.AreaMode;

    public override int GetHashCode()
        => HashCode.Combine(Sid, AreaMode);

    public static bool operator ==(PlayerMap left, PlayerMap right)
        => left.Equals(right);

    public static bool operator !=(PlayerMap left, PlayerMap right)
        => !(left == right);

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Sid);
        if (Sid.Length != 0)
            writer.Write((byte)AreaMode);
    }

    public static PlayerMap Deserialize(ref RefBinaryReader reader)
    {
        string sid = reader.ReadString();
        AreaMode areaMode = sid.Length == 0 ? AreaMode.Normal : (AreaMode)reader.ReadByte();
        return new PlayerMap(sid, areaMode);
    }
}