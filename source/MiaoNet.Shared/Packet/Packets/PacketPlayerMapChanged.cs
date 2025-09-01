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

    public PlayerStats? InitialStats { get; }

    public PacketPlayerMapChanged(string mapSid, string mapRoom, PlayerStats? initialStats)
    {
        MapSid = mapSid;
        MapRoom = mapRoom;
        InitialStats = initialStats;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(MapSid);
        writer.Write(MapRoom);

        writer.Write(InitialStats is not null);
        if (InitialStats is not null)
            writer.Write(InitialStats);
    }

    public static PacketPlayerMapChanged Deserialize(ref RefBinaryReader reader)
    {
        var mapSid = reader.ReadString();
        var mapRoom = reader.ReadString();
        PlayerStats? initialStats = null;

        if (reader.ReadBoolean())
            initialStats = reader.Read<PlayerStats>();
        return new(mapSid, mapRoom, initialStats);
    }
}

public sealed class PacketPlayerMapChangedNotify : PacketPlayerNotify<PacketPlayerMapChanged>, IPacket<PacketPlayerMapChangedNotify>
{
    [Flags]
    public enum DataFlags : byte
    {
        HasGraphicsInfo = 1 << 0,
        HasPlayerInitialStats = 1 << 1
    }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerStats? PlayerInitialStats { get; set; }

    public PacketPlayerMapChangedNotify(int playerID, PacketPlayerMapChanged packet)
        : base(playerID, packet)
    {
    }

    public PacketPlayerMapChangedNotify(
        int playerID, PacketPlayerMapChanged packet,
        PlayerGraphicsInfo? graphicsInfo,
        PlayerStats? initialStats
    ) : this(playerID, packet)
    {
        GraphicsInfo = graphicsInfo;
        PlayerInitialStats = initialStats;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        base.Serialize(ref writer);
        DataFlags flags = 0;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (PlayerInitialStats is not null) flags |= DataFlags.HasPlayerInitialStats;

        writer.Write((byte)flags);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (PlayerInitialStats is not null) writer.Write(PlayerInitialStats);
    }

    public static PacketPlayerMapChangedNotify Deserialize(ref RefBinaryReader reader)
    {
        int playerID = reader.ReadInt32();
        PacketPlayerMapChanged packet = reader.Read<PacketPlayerMapChanged>();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerStats? initialStats = null;

        DataFlags dataFlags = (DataFlags)reader.ReadByte();
        if (dataFlags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (dataFlags.HasFlag(DataFlags.HasPlayerInitialStats))
            initialStats = reader.Read<PlayerStats>();

        return new(playerID, packet, graphicsInfo, initialStats);
    }
}