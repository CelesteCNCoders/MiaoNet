namespace MiaoNet.Shared;

public sealed class PacketPlayerJoined : IPacket<PacketPlayerJoined>
{
    [Flags]
    public enum DataFlags : byte
    {
        HasGraphicsInfo = 1 << 0,
        HasPlayerInitialStats = 1 << 1
    }

    public ChannelPlayerStateInfo Info { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerStats? InitialStats { get; set; }

    public PacketPlayerJoined(ChannelPlayerStateInfo info)
    {
        Info = info;
    }

    public PacketPlayerJoined(ChannelPlayerStateInfo info, PlayerGraphicsInfo? graphicsInfo, PlayerStats? initialStats)
        : this(info)
    {
        GraphicsInfo = graphicsInfo;
        InitialStats = initialStats;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Info);

        DataFlags flags = 0;
        if (GraphicsInfo is not null) flags |= DataFlags.HasGraphicsInfo;
        if (InitialStats is not null) flags |= DataFlags.HasPlayerInitialStats;
        writer.Write((byte)flags);
        if (GraphicsInfo is not null) writer.Write(GraphicsInfo);
        if (InitialStats is not null) writer.Write(InitialStats);
    }

    public static PacketPlayerJoined Deserialize(ref RefBinaryReader reader)
    {
        var info = reader.Read<ChannelPlayerStateInfo>();
        PlayerGraphicsInfo? graphicsInfo = null;
        PlayerStats? initialStats = null;

        DataFlags flags = (DataFlags)reader.ReadByte();
        if (flags.HasFlag(DataFlags.HasGraphicsInfo))
            graphicsInfo = reader.Read<PlayerGraphicsInfo>();
        if (flags.HasFlag(DataFlags.HasPlayerInitialStats))
            initialStats = reader.Read<PlayerStats>();
        return new(info, graphicsInfo, initialStats);
    }
}