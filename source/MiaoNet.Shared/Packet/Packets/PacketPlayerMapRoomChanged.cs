namespace MiaoNet.Shared;

// used when room changed
// NOT used when changing between in debug map and not in debug map
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

public sealed class PacketPlayerMapRoomChangedNotification : PacketPlayerNotification<PacketPlayerMapRoomChanged>,
    IPacket<PacketPlayerMapRoomChangedNotification>
{
    public PacketPlayerMapRoomChangedNotification(int playerID, PacketPlayerMapRoomChanged packet)
        : base(playerID, packet)
    {
    }

    public static PacketPlayerMapRoomChangedNotification Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PacketPlayerMapRoomChanged>());
}