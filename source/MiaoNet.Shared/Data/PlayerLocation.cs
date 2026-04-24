namespace MiaoNet.Shared;

public struct PlayerLocation : IRefBinarySerializable<PlayerLocation>, IEquatable<PlayerLocation>
{
    public string MapSid { get; set; } // empty: player is not in level

    // only meaningful when MapSid is not null
    public AreaMode Side { get; set; }

    public readonly char SideCharacter => (char)('A' + (char)Side);

    public string MapRoom { get; set; } // empty: player is not in level or is in debug map

    /// <summary>
    /// <see cref="MapSid"/> is <see cref="string.Empty"/> and <see cref="MapRoom"/> is <see cref="string.Empty"/>
    /// </summary>
    public readonly bool IsEmpty => MapSid == string.Empty && MapRoom == string.Empty;

    /// <summary>
    /// <see cref="MapSid"/> is <b>NOT</b> <see cref="string.Empty"/> and <see cref="MapRoom"/> is <see cref="string.Empty"/>
    /// </summary>
    public readonly bool IsInDebugMap => MapSid != string.Empty && MapRoom == string.Empty;

    /// <summary>
    /// <see cref="MapSid"/> is <b>NOT</b> <see cref="string.Empty"/> and <see cref="MapRoom"/> is <b>NOT</b> <see cref="string.Empty"/>
    /// </summary>
    public readonly bool IsInMap => MapSid != string.Empty && MapRoom != string.Empty;

    /// <summary>
    /// Both <see cref="MapSid"/> and <see cref="MapRoom"/> is <b>NOT</b> <see langword="null"/>
    /// </summary>
    public readonly bool IsValid => MapSid != null && MapRoom != null;

    public static PlayerLocation Empty => new(string.Empty, AreaMode.Normal, string.Empty);

    public PlayerLocation(string mapSid, AreaMode side, string mapRoom)
    {
        SafeGuard.Assert(mapSid != null);
        SafeGuard.Assert(mapRoom != null);
        MapSid = mapSid;
        Side = side;
        MapRoom = mapRoom;
        if (mapSid == string.Empty)
            SafeGuard.Assert(MapRoom == string.Empty);
    }

#if MIAO_CLIENT
    public PlayerLocation(AreaKey areaKey, string mapRoom)
        : this(areaKey.SID, areaKey.Mode, mapRoom)
    {
    }
#endif

    public readonly override string ToString()
    {
        if (MapSid == string.Empty)
            return "None";
        if (MapRoom == string.Empty)
            return $"{MapSid} {SideCharacter} .DebugMap";
        return $"{MapSid} {SideCharacter} {MapRoom}";
    }

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(MapSid);
        writer.Write((byte)Side);
        writer.Write(MapRoom);
    }

    public static PlayerLocation Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), (AreaMode)reader.ReadByte(), reader.ReadString());

    public readonly override bool Equals(object? obj)
        => obj is PlayerLocation loc && Equals(loc);

    public readonly bool Equals(PlayerLocation other)
        => MapSid == other.MapSid &&
           Side == other.Side &&
           MapRoom == other.MapRoom;

    public readonly override int GetHashCode()
        => HashCode.Combine(MapSid, Side, MapRoom);

    public static bool operator ==(PlayerLocation left, PlayerLocation right)
        => left.Equals(right);

    public static bool operator !=(PlayerLocation left, PlayerLocation right)
        => !(left == right);

    public enum ChangeResult { None, RoomOnly, All }

    public readonly ChangeResult CompareTo(PlayerLocation other)
    {
        if (this == other)
            return ChangeResult.None;

        if (IsSameMapWith(other) && MapRoom != other.MapRoom)
            return ChangeResult.RoomOnly;
        else
            return ChangeResult.All;
    }

    public readonly bool IsSameMapWith(PlayerLocation other)
        => MapSid == other.MapSid && Side == other.Side;

#if MIAO_CLIENT
    public static PlayerLocation FetchFrom(Session session)
        => new(session.Area, session.Level);
#endif
}