namespace MiaoNet.Shared;

public sealed class PacketPlayerJoined : IPacket<PacketPlayerJoined>
{
    [Flags]
    public enum DataFlags : byte
    {
        HasGraphicsInfo = 1 << 0,
        HasPlayerInitialState = 1 << 1
    }

    public ChannelPlayerLocationInfo Info { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerJoined(ChannelPlayerLocationInfo info)
    {
        Info = info;
    }

    public PacketPlayerJoined(ChannelPlayerLocationInfo info, PlayerGraphicsInfo? graphicsInfo, PlayerState? initialState)
        : this(info)
    {
        GraphicsInfo = graphicsInfo;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Info);

        DataFlags flags = 0;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (InitialState is not null) flags |= DataFlags.HasPlayerInitialState;
        writer.Write((byte)flags);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (InitialState is not null) writer.Write(InitialState);
    }

    public static PacketPlayerJoined Deserialize(ref RefBinaryReader reader)
    {
        var info = reader.Read<ChannelPlayerLocationInfo>();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerState? initialState = null;

        DataFlags flags = (DataFlags)reader.ReadByte();
        if (flags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (flags.HasFlag(DataFlags.HasPlayerInitialState))
            initialState = reader.Read<PlayerState>();
        return new(info, graphicsInfo, initialState);
    }
}