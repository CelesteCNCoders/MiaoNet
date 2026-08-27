namespace MiaoNet.Shared;

// client to server
public sealed class PacketPlayerChannelMove : IContextualPacket<PacketPlayerChannelMove>
{
    public string TargetChannelName { get; }

    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public PlayerState? InitialState { get; }

    public PacketPlayerChannelMove(
        string targetChannelName,
        uint playerEpoch,
        uint playerSequence,
        PlayerState? initialState
    )
    {
        TargetChannelName = targetChannelName;
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(TargetChannelName);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerChannelMove Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        return new(
            reader.ReadString(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadNullable<PlayerState, PooledStringManager>(context.PooledStringManager)
        );
    }
}

// server to client
public sealed class PacketPlayerChannelMovedNotification : PacketPlayerNotification,
    IContextualPacket<PacketPlayerChannelMovedNotification>
{
    public int ChannelID { get; }

    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    // in-map data (ghost state) of the moved player; sent to same-map receivers
    public PlayerMovedInitialData? InitialData { get; }

    // "summary" data (location + global flags); sent to same-channel receivers
    public PlayerPresenceData? Presence { get; }

    public PacketPlayerChannelMovedNotification(
        int playerID,
        int channelID,
        uint playerEpoch,
        uint playerSequence
    )
        : this(playerID, channelID, playerEpoch, playerSequence, null, null)
    {
    }

    public PacketPlayerChannelMovedNotification(
        int playerID,
        int channelID,
        uint playerEpoch,
        uint playerSequence,
        PlayerMovedInitialData? initialData,
        PlayerPresenceData? presence
    ) : base(playerID)
    {
        ChannelID = channelID;
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        InitialData = initialData;
        Presence = presence;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(ChannelID);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
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
        uint playerEpoch = reader.ReadUInt32();
        uint playerSequence = reader.ReadUInt32();
        PlayerMovedInitialData? initialData = reader.ReadBoolean()
            ? reader.Read<PlayerMovedInitialData, PooledStringManager>(context.PooledStringManager)
            : null;
        PlayerPresenceData? presence = reader.ReadBoolean()
            ? reader.Read<PlayerPresenceData>()
            : null;

        return new(playerID, channelID, playerEpoch, playerSequence, initialData, presence);
    }
}
