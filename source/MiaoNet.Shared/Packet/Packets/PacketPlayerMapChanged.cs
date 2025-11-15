using System.Diagnostics;

namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChanged : IPacket<PacketPlayerMapChanged>
{
    public PlayerLocation Location { get; set; }

    public PlayerState? InitialState { get; }

    public PacketPlayerMapChanged(PlayerLocation location, PlayerState? initialState)
    {
        Debug.Assert(!location.IsEmpty);

        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);

        writer.Write(InitialState is not null);
        if (InitialState is not null)
            writer.Write(InitialState);
    }

    public static PacketPlayerMapChanged Deserialize(ref RefBinaryReader reader)
    {
        var loc = reader.Read<PlayerLocation>();
        PlayerState? initialStats = null;

        if (reader.ReadBoolean())
            initialStats = reader.Read<PlayerState>();
        return new(loc, initialStats);
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

    public PlayerLocation Location { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerMapChangedNotification(int playerID, PlayerLocation location)
        : base(playerID)
    {
        Location = location;
    }

    public PacketPlayerMapChangedNotification(
        int playerID, PlayerLocation location,
        PlayerGraphicsInfo? graphicsInfo,
        PlayerState? initialStats
    ) : this(playerID, location)
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
        writer.Write(Location);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (InitialState is not null) writer.Write(InitialState);
    }

    public static PacketPlayerMapChangedNotification Deserialize(ref RefBinaryReader reader)
    {
        int playerID = reader.ReadInt32();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerState? initialStats = null;

        DataFlags dataFlags = (DataFlags)reader.ReadByte();

        PlayerLocation location = reader.Read<PlayerLocation>();

        if (dataFlags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (dataFlags.HasFlag(DataFlags.HasInitialStats))
            initialStats = reader.Read<PlayerState>();

        return new(playerID, location, graphicsInfo, initialStats);
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