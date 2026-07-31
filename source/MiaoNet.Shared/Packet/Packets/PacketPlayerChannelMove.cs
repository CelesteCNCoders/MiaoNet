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
    public int ChannelID { get; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerChannelMovedNotification(int playerID, int channelID)
        : base(playerID)
    {
        ChannelID = channelID;
    }

    public PacketPlayerChannelMovedNotification(
        int playerID, int channelID, PlayerState? initialState
    ) : this(playerID, channelID)
    {
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(ChannelID);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerChannelMovedNotification Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        int playerID = reader.ReadInt32();
        int channelID = reader.ReadInt32();
        PlayerState? initialState = reader.ReadNullable<PlayerState, PooledStringManager>(context.PooledStringManager);

        return new(playerID, channelID, initialState);
    }
}