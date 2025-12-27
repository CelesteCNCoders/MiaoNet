using System.Diagnostics;
#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif

namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChanged : IContextualPacket<PacketPlayerMapChanged>
{
    public PlayerLocation Location { get; set; }

    public PlayerState? InitialState { get; }

    public PacketPlayerMapChanged(PlayerLocation location, PlayerState? initialState)
    {
        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(Location);

        if (InitialState is not null)
        {
            writer.Write(true);
            writer.Write(InitialState, context.PooledStringManager);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static PacketPlayerMapChanged Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new PacketPlayerMapChanged(
            reader.Read<PlayerLocation>(),
            reader.ReadBoolean()
                ? reader.Read<PlayerState, PooledStringManager>(context.PooledStringManager)
                : null
        );
}

public sealed class PacketPlayerMapChangedNotification : PacketPlayerNotification,
    IContextualPacket<PacketPlayerMapChangedNotification>
{
    [Flags]
    public enum DataFlags : byte
    {
        None = 0,
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

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);

        DataFlags flags = DataFlags.None;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (InitialState is not null) flags |= DataFlags.HasInitialStats;

        writer.Write((byte)flags);
        writer.Write(Location);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (InitialState is not null) writer.Write(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerMapChangedNotification Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        int playerID = reader.ReadInt32();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerState? initialStats = null;

        DataFlags dataFlags = (DataFlags)reader.ReadByte();

        PlayerLocation location = reader.Read<PlayerLocation>();

        if (dataFlags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (dataFlags.HasFlag(DataFlags.HasInitialStats))
            initialStats = reader.Read<PlayerState, PooledStringManager>(context.PooledStringManager);

        return new(playerID, location, graphicsInfo, initialStats);
    }
}