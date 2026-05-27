namespace MiaoNet.Shared;

// client to server
public sealed class PacketPlayerChannelMove : IContextlessPacket<PacketPlayerChannelMove>
{
    public int TargetChannelID { get; }

    public PacketPlayerChannelMove(int targetChannelID)
    {
        TargetChannelID = targetChannelID;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(TargetChannelID);
    }

    public static PacketPlayerChannelMove Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadInt32());
    }
}

// server to client
public sealed class PacketPlayerChannelMovedNotification : PacketPlayerNotification,
    IContextualPacket<PacketPlayerChannelMovedNotification>
{
    [Flags]
    public enum DataFlags : byte
    {
        None = 0,
        HasGraphicsInfo = 1 << 0,
        HasInitialState = 1 << 1
    }

    public int ChannelID { get; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerChannelMovedNotification(int playerID, int channelID)
        : base(playerID)
    {
        ChannelID = channelID;
    }

    public PacketPlayerChannelMovedNotification(
        int playerID, int channelID,
        PlayerGraphicsInfo? graphicsInfo, PlayerState? initialState
    ) : this(playerID, channelID)
    {
        GraphicsInfo = graphicsInfo;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);

        DataFlags flags = DataFlags.None;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (InitialState is not null) flags |= DataFlags.HasInitialState;

        writer.Write((byte)flags);
        writer.Write(ChannelID);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (InitialState is not null) writer.Write(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerChannelMovedNotification Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        int playerID = reader.ReadInt32();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerState? initialStats = null;

        DataFlags dataFlags = (DataFlags)reader.ReadByte();

        int channelID = reader.ReadInt32();

        if (dataFlags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (dataFlags.HasFlag(DataFlags.HasInitialState))
            initialStats = reader.Read<PlayerState, PooledStringManager>(context.PooledStringManager);

        return new(playerID, channelID, graphicsInfo, initialStats);
    }
}