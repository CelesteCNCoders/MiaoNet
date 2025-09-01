using System.Diagnostics;

namespace MiaoNet.Shared;

public sealed class PlayerStateInfo : IRefBinarySerializable<PlayerStateInfo>
{
    public string MapSid { get; set; } // empty: player is not in level

    public string MapRoom { get; set; } // empty: player is not in level or is in debug map

    public PlayerStateInfo(string mapSid, string mapRoom)
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

    public static PlayerStateInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString(), reader.ReadString());
}