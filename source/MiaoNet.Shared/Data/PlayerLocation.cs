namespace MiaoNet.Shared;

public readonly struct PlayerLocation : IRefBinarySerializable<PlayerLocation>, IEquatable<PlayerLocation>
{
    public PlayerMap Map { get; }

    public string Room { get; }

    public readonly bool IsEmpty => Map.IsEmpty && Room.Length == 0;

    public readonly bool IsInDebugMap => !Map.IsEmpty && Room.Length == 0;

    public readonly bool IsInMap => !Map.IsEmpty && Room != string.Empty;

    public static PlayerLocation Empty => new(string.Empty, AreaMode.Normal, string.Empty);

    public PlayerLocation(PlayerMap map, string room)
    {
        if (map.IsEmpty)
            SafeGuard.Assert(room.Length == 0);
        Map = map;
        Room = room;
    }

    public PlayerLocation(string mapSid, AreaMode areaMode, string room)
    {
        Map = new(mapSid, areaMode);
        Room = room;
    }

#if MIAO_CLIENT
    public PlayerLocation(AreaKey areaKey, string mapRoom)
        : this(areaKey.SID, areaKey.Mode, mapRoom)
    {
    }
#endif

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Map);
        if (!Map.IsEmpty)
            writer.Write(Room);
    }

    public static PlayerLocation Deserialize(ref RefBinaryReader reader)
    {
        PlayerMap map = reader.Read<PlayerMap>();
        string room = map.IsEmpty ? string.Empty : reader.ReadString();
        return new PlayerLocation(map, room);
    }

    public readonly override bool Equals(object? obj)
        => obj is PlayerLocation loc && Equals(loc);

    public readonly bool Equals(PlayerLocation other)
        => Map == other.Map && Room == other.Room;

    public readonly override int GetHashCode()
        => HashCode.Combine(Map, Room);

    public static bool operator ==(PlayerLocation left, PlayerLocation right)
        => left.Equals(right);

    public static bool operator !=(PlayerLocation left, PlayerLocation right)
        => !(left == right);

    public enum ChangeResult { None, RoomOnly, All }

    public readonly ChangeResult CompareTo(PlayerLocation other)
    {
        if (this == other)
            return ChangeResult.None;

        if (Map == other.Map && Room != other.Room)
            return ChangeResult.RoomOnly;
        else
            return ChangeResult.All;
    }

    public override string ToString()
    {
        if (Map.IsEmpty)
            return "None";
        string roomString = Room.Length == 0 ? ".DebugMap" : Room;
        return $"{Map.Sid} {Map.AreaModeCharacter} {roomString}";
    }

#if MIAO_CLIENT
    public static PlayerLocation FetchFrom(Session session)
        => new(session.Area, session.Level);
#endif
}