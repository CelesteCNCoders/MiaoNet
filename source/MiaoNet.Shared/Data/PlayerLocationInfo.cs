using System.Diagnostics;

namespace MiaoNet.Shared;

public sealed class PlayerLocationInfo : IRefBinarySerializable<PlayerLocationInfo>, IEquatable<PlayerLocationInfo?>
{
    public string MapSid { get; set; } // empty: player is not in level

    public string MapRoom { get; set; } // empty: player is not in level or is in debug map

    public PlayerLocationInfo(string mapSid, string mapRoom)
    {
        MapSid = mapSid;
        MapRoom = mapRoom;
        if (mapSid == string.Empty)
            Debug.Assert(MapRoom == string.Empty);
    }

    public override string ToString()
        => $"{MapSid}.{MapRoom}";

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(MapSid);
        writer.Write(MapRoom);
    }

    public static PlayerLocationInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), reader.ReadString());

    public void UpdateWith(string mapSid, string mapRoom)
    {
        MapRoom = mapRoom;
        if (!string.IsNullOrEmpty(mapSid) || string.IsNullOrEmpty(mapRoom))
        {
            MapSid = mapSid;
        }
    }

    public override bool Equals(object? obj)
        => Equals(obj as PlayerLocationInfo);

    public bool Equals(PlayerLocationInfo? other)
        => other is not null &&
           MapSid == other.MapSid &&
           MapRoom == other.MapRoom;

    public override int GetHashCode()
        => HashCode.Combine(MapSid, MapRoom);

    public static bool operator ==(PlayerLocationInfo? left, PlayerLocationInfo? right)
        => EqualityComparer<PlayerLocationInfo>.Default.Equals(left, right);

    public static bool operator !=(PlayerLocationInfo? left, PlayerLocationInfo? right)
        => !(left == right);
}