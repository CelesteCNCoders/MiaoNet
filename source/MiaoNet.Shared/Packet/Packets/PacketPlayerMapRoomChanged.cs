namespace MiaoNet.Shared;

// TODO level 2 update & graphics?
public sealed class PacketPlayerMapRoomChanged : IPacket<PacketPlayerMapRoomChanged>
{
    public string MapRoom { get; }

    public PacketPlayerMapRoomChanged(string mapRoom)
    {
        MapRoom = mapRoom;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(MapRoom);


    public static PacketPlayerMapRoomChanged Deserialize(ref RefBinaryReader reader)
    {
        var mapRoom = reader.ReadString();
        return new(mapRoom);
    }
}

public sealed class PacketPlayerMapRoomChangedNotify : PacketPlayerNotify<PacketPlayerMapRoomChanged>,
    IPacket<PacketPlayerMapRoomChangedNotify>
{
    public PacketPlayerMapRoomChangedNotify(int playerID, PacketPlayerMapRoomChanged packet)
        : base(playerID, packet)
    {
    }

    public static PacketPlayerMapRoomChangedNotify Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PacketPlayerMapRoomChanged>());
}