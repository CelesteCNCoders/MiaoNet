using System.Diagnostics;

namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChanged : IPacket<PacketPlayerMapChanged>
{
    public string MapSid { get; }

    public string MapRoom { get; } // can be string.Empty (player is in debug map)

    public PlayerState? InitialState { get; }

    public PacketPlayerMapChanged(string mapSid, string mapRoom, PlayerState? initialState)
    {
        Debug.Assert(!string.IsNullOrEmpty(mapSid));

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

public sealed class PacketPlayerMapChangedNotification : PacketPlayerNotification,
    IPacket<PacketPlayerMapChangedNotification>
{
    [Flags]
    public enum DataFlags : byte
    {
        HasGraphicsInfo = 1 << 0,
        HasInitialStats = 1 << 1
    }

    public string MapSid { get; set; }

    public string MapRoom { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerMapChangedNotification(int playerID, string mapSid, string mapRoom)
        : base(playerID)
    {
        MapSid = mapSid;
        MapRoom = mapRoom;
    }

    public PacketPlayerMapChangedNotification(
        int playerID, string mapSid, string mapRoom,
        PlayerGraphicsInfo? graphicsInfo,
        PlayerState? initialStats
    ) : this(playerID, mapSid, mapRoom)
    {
        GraphicsInfo = graphicsInfo;
        InitialState = initialStats;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        base.Serialize(ref writer);

        DataFlags flags = 0;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (InitialState is not null) flags |= DataFlags.HasInitialStats;

        writer.Write((byte)flags);
        writer.Write(MapSid);
        writer.Write(MapRoom);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (InitialState is not null) writer.Write(InitialState);
    }

    public static PacketPlayerMapChangedNotification Deserialize(ref RefBinaryReader reader)
    {
        int playerID = reader.ReadInt32();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerState? initialStats = null;

        DataFlags dataFlags = (DataFlags)reader.ReadByte();

        string mapSid = reader.ReadString();
        string mapRoom = reader.ReadString();

        if (dataFlags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (dataFlags.HasFlag(DataFlags.HasInitialStats))
            initialStats = reader.Read<PlayerState>();

        return new(playerID, mapSid, mapRoom, graphicsInfo, initialStats);
    }
}

public sealed class PacketPlayerMapChangedResponse : IPacket<PacketPlayerMapChangedResponse>
{
    public readonly struct Player : IRefBinarySerializable<Player>
    {
        public int PlayerID { get; }

        public PlayerState State { get; }

        public PlayerGraphicsInfo? GraphicsInfo { get; }

        public Player(int playerID, PlayerState state, PlayerGraphicsInfo? graphicsInfo)
        {
            PlayerID = playerID;
            State = state;
            GraphicsInfo = graphicsInfo;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(PlayerID);
            writer.Write(State);
            if (GraphicsInfo is not null)
            {
                writer.Write(true);
                writer.Write(GraphicsInfo);
            }
            else
            {
                writer.Write(false);
            }
        }

        public static Player Deserialize(ref RefBinaryReader reader)
            => new(
                reader.ReadInt32(), 
                reader.Read<PlayerState>(),
                reader.ReadBoolean() ? reader.Read<PlayerGraphicsInfo>() : null
            );
    }

    public List<Player> PlayersInMap { get; }

    public PacketPlayerMapChangedResponse(List<Player> playersInMap)
    {
        PlayersInMap = playersInMap;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayersInMap);
    }

    public static PacketPlayerMapChangedResponse Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadList<Player>());
}