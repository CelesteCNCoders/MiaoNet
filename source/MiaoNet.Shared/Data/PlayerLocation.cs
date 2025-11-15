using System.Diagnostics;

namespace MiaoNet.Shared;

public struct PlayerLocation : IRefBinarySerializable<PlayerLocation>, IEquatable<PlayerLocation>
{
    public string MapSid { get; set; } // empty: player is not in level

    public string MapRoom { get; set; } // empty: player is not in level or is in debug map

    public readonly string MapSet => MapSid == string.Empty ? string.Empty : MapSid[..MapSid.IndexOf('/')];

    public readonly bool IsEmpty => MapSid == string.Empty && MapRoom == string.Empty;

    public readonly bool IsInDebugMap => MapSid != string.Empty && MapRoom != string.Empty;

    public readonly bool IsInMap => MapSid != string.Empty && MapRoom != string.Empty;

    public static PlayerLocation Empty => new(string.Empty, string.Empty);

    [Obsolete("Use PlayerLocation.Empty instead.")]
    public PlayerLocation() { MapSid = MapRoom = string.Empty; }

    public PlayerLocation(string mapSid, string mapRoom)
    {
        MapSid = mapSid;
        MapRoom = mapRoom;
        if (mapSid == string.Empty)
            SafeGuard.Assert(MapRoom == string.Empty);
    }

    public readonly override string ToString()
        => $"{MapSid}.{MapRoom}";

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(MapSid);
        writer.Write(MapRoom);
    }

    public static PlayerLocation Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), reader.ReadString());

    public readonly override bool Equals(object? obj)
        => obj is PlayerLocation loc && Equals(loc);

    public readonly bool Equals(PlayerLocation other)
        => MapSid == other.MapSid &&
           MapRoom == other.MapRoom;

    public readonly override int GetHashCode()
        => HashCode.Combine(MapSid, MapRoom);

    public static bool operator ==(PlayerLocation left, PlayerLocation right)
        => left.Equals(right);

    public static bool operator !=(PlayerLocation left, PlayerLocation right)
        => !(left == right);

    public enum ChangedResult { None, RoomOnly, All }

    public readonly ChangedResult CompareTo(PlayerLocation other)
        => this == other ? ChangedResult.None :
           MapSid == other.MapSid && MapRoom != other.MapRoom ?
           ChangedResult.RoomOnly :
           ChangedResult.All;

    public readonly bool SameMapWith(PlayerLocation other)
        => MapSid == other.MapSid;

#if MIAO_CLIENT
    public static PlayerLocation FetchFrom(Session session)
        => new(session.Area.SID, session.Level);
#endif
}