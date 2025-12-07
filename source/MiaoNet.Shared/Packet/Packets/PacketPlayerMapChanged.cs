using System.Diagnostics;
#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif

namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChanged : IPacket<PacketPlayerMapChanged>
{
    public PlayerLocation Location { get; set; }

    public PlayerState? InitialState { get; }

    public PacketPlayerMapChanged(PlayerLocation location, PlayerState? initialState)
    {
        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);

        if (InitialState is not null)
        {
            writer.Write(true);
            writer.Write(InitialState);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static PacketPlayerMapChanged Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<PlayerLocation>(), reader.ReadBoolean() ? reader.Read<PlayerState>() : null);
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