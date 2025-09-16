namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChanged : IPacket<PacketPlayerMapChanged>
{
    // if:
    // MapSid is empty && MapRoom is empty -> Player went to menu
    // MapSid is empty && MapRoom is NOT empty -> Player went to a new room in the map
    // MapSid is NOT empty && MapRoom is empty -> Player opened debug map in a map
    // MapSid is NOT empty && MapRoom is NOT empty -> Player went to a new map

    public string MapSid { get; }

    public string MapRoom { get; }

    public PlayerState? InitialState { get; }

    public PacketPlayerMapChanged(string mapSid, string mapRoom, PlayerState? initialState)
    {
        MapSid = mapSid;
        MapRoom = mapRoom;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(MapSid);
        writer.Write(MapRoom);

        writer.Write(InitialState is not null);
        if (InitialState is not null)
            writer.Write(InitialState);
    }

    public static PacketPlayerMapChanged Deserialize(ref RefBinaryReader reader)
    {
        var mapSid = reader.ReadString();
        var mapRoom = reader.ReadString();
        PlayerState? initialStats = null;

        if (reader.ReadBoolean())
            initialStats = reader.Read<PlayerState>();
        return new(mapSid, mapRoom, initialStats);
    }
}

public sealed class PacketPlayerMapChangedNotify : PacketPlayerNotify,
    IPacket<PacketPlayerMapChangedNotify>
{
    [Flags]
    public enum DataFlags : byte
    {
        HasGraphicsInfo = 1 << 0,
        HasPlayerInitialStats = 1 << 1,
        HasMapSid = 1 << 2,
        HasMapRoom = 1 << 3
    }

    public string MapSid { get; set; }

    public string MapRoom { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerState? PlayerInitialState { get; set; }

    public PacketPlayerMapChangedNotify(int playerID, string mapSid, string mapRoom)
        : base(playerID)
    {
        MapSid = mapSid;
        MapRoom = mapRoom;
    }

    public PacketPlayerMapChangedNotify(
        int playerID, string mapSid, string mapRoom,
        PlayerGraphicsInfo? graphicsInfo,
        PlayerState? initialStats
    ) : this(playerID, mapSid, mapRoom)
    {
        GraphicsInfo = graphicsInfo;
        PlayerInitialState = initialStats;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        base.Serialize(ref writer);

        DataFlags flags = 0;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (PlayerInitialState is not null) flags |= DataFlags.HasPlayerInitialStats;
        if (!string.IsNullOrEmpty(MapSid)) flags |= DataFlags.HasMapSid;
        if (!string.IsNullOrEmpty(MapRoom)) flags |= DataFlags.HasMapRoom;

        writer.Write((byte)flags);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (PlayerInitialState is not null) writer.Write(PlayerInitialState);
        if (!string.IsNullOrEmpty(MapSid)) writer.Write(MapSid);
        if (!string.IsNullOrEmpty(MapRoom)) writer.Write(MapRoom);
    }

    public static PacketPlayerMapChangedNotify Deserialize(ref RefBinaryReader reader)
    {
        int playerID = reader.ReadInt32();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerState? initialStats = null;

        DataFlags dataFlags = (DataFlags)reader.ReadByte();
        if (dataFlags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (dataFlags.HasFlag(DataFlags.HasPlayerInitialStats))
            initialStats = reader.Read<PlayerState>();
        string mapSid = dataFlags.HasFlag(DataFlags.HasMapSid) ?
            reader.ReadString() : string.Empty;
        string mapRoom = dataFlags.HasFlag(DataFlags.HasMapRoom) ?
            reader.ReadString() : string.Empty;

        return new(playerID, mapSid, mapRoom, graphicsInfo, initialStats);
    }
}