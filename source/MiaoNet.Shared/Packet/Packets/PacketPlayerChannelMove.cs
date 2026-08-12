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

    // in-map data (ghost state) of the moved player; sent to same-map receivers
    public PlayerMovedInitialData? InitialData { get; }

    // "summary" data (location + global flags); sent to same-channel receivers
    public PlayerPresenceData? Presence { get; }

    public PacketPlayerChannelMovedNotification(int playerID, int channelID)
        : this(playerID, channelID, null, null)
    {
    }

    public PacketPlayerChannelMovedNotification(
        int playerID,
        int channelID,
        PlayerMovedInitialData? initialData,
        PlayerPresenceData? presence
    ) : base(playerID)
    {
        ChannelID = channelID;
        InitialData = initialData;
        Presence = presence;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(ChannelID);
        if (InitialData is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(InitialData.Value, context.PooledStringManager);
        }
        if (Presence is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(Presence.Value);
        }
    }

    public static PacketPlayerChannelMovedNotification Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        int playerID = reader.ReadInt32();
        int channelID = reader.ReadInt32();
        PlayerMovedInitialData? initialData = reader.ReadBoolean()
            ? reader.Read<PlayerMovedInitialData, PooledStringManager>(context.PooledStringManager)
            : null;
        PlayerPresenceData? presence = reader.ReadBoolean()
            ? reader.Read<PlayerPresenceData>()
            : null;

        return new(playerID, channelID, initialData, presence);
    }
}