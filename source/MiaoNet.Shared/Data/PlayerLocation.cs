using System.Diagnostics;
#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif

namespace MiaoNet.Shared;

public struct PlayerLocation : IRefBinarySerializable<PlayerLocation>, IEquatable<PlayerLocation>
{
    public string MapSid { get; set; } // empty: player is not in level

    // only meaningful when MapSid is not null
    public AreaMode MapSide { get; set; }

    public string MapRoom { get; set; } // empty: player is not in level or is in debug map

    public readonly string MapSet => MapSid == string.Empty ? string.Empty : MapSid[..MapSid.IndexOf('/')];

    /// <summary><see cref="MapSid"/> is Empty and <see cref="MapRoom"/> is Empty</summary>
    public readonly bool IsEmpty => MapSid == string.Empty && MapRoom == string.Empty;

    /// <summary><see cref="MapSid"/> is <b>NOT</b> Empty and <see cref="MapRoom"/> is Empty</summary>
    public readonly bool IsInDebugMap => MapSid != string.Empty && MapRoom == string.Empty;

    /// <summary><see cref="MapSid"/> is <b>NOT</b> Empty and <see cref="MapRoom"/> is <b>NOT</b> Empty</summary>
    public readonly bool IsInMap => MapSid != string.Empty && MapRoom != string.Empty;

    public static PlayerLocation Empty => new(string.Empty, AreaMode.Normal, string.Empty);

    public PlayerLocation(string mapSid, AreaMode mapSide, string mapRoom)
    {
        MapSid = mapSid;
        MapSide = mapSide;
        MapRoom = mapRoom;
        if (mapSid == string.Empty)
            SafeGuard.Assert(MapRoom == string.Empty);
    }

    public readonly override string ToString()
        => $"{MapSid}.{MapRoom} {(char)('A' + (char)MapSide)}";

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(MapSid);
        writer.Write((byte)MapSide);
        writer.Write(MapRoom);
    }

    public static PlayerLocation Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), (AreaMode)reader.ReadByte(), reader.ReadString());

    public readonly override bool Equals(object? obj)
        => obj is PlayerLocation loc && Equals(loc);

    public readonly bool Equals(PlayerLocation other)
        => MapSid == other.MapSid &&
           MapSide == other.MapSide &&
           MapRoom == other.MapRoom;

    public readonly override int GetHashCode()
        => HashCode.Combine(MapSid, MapSide, MapRoom);

    public static bool operator ==(PlayerLocation left, PlayerLocation right)
        => left.Equals(right);

    public static bool operator !=(PlayerLocation left, PlayerLocation right)
        => !(left == right);

    public enum ChangeResult { None, RoomOnly, FromDebugMap, All }

    public readonly ChangeResult CompareTo(PlayerLocation other)
    {
        if (this == other)
            return ChangeResult.None;

        if (IsSameMapWith(other) && MapRoom != other.MapRoom)
        {
            return MapRoom == string.Empty
                    ? ChangeResult.FromDebugMap
                    : ChangeResult.RoomOnly;
        }
        else
        {
            return ChangeResult.All;
        }
    }

    public readonly bool IsSameMapWith(PlayerLocation other)
        => MapSid == other.MapSid && MapSide == other.MapSide;

#if MIAO_CLIENT
    public static PlayerLocation FetchFrom(Session session)
        => new(session.Area.SID, session.Area.Mode, session.Level);
#endif
}