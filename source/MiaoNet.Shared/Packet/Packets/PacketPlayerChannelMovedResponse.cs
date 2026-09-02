namespace MiaoNet.Shared;

public sealed class PacketPlayerChannelMovedResponse : IContextualPacket<PacketPlayerChannelMovedResponse>
{
    public int ChannelID { get; }

    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    // in map data, such as PlayerState and PlayerGraphicsInfo(not impled currently)
    public IReadOnlyCollection<PlayerMovedInitialDataWithID>? Players { get; }

    // "summary" data, such as PlayerLocationInfo and GlobalFlags(paused, taking golden...)
    public IReadOnlyCollection<PlayerPresenceDataWithID>? ChannelPlayers { get; }

    public PacketPlayerChannelMovedResponse(
        int channelID,
        uint playerEpoch,
        uint playerSequence,
        IReadOnlyCollection<PlayerMovedInitialDataWithID>? players,
        IReadOnlyCollection<PlayerPresenceDataWithID>? channelPlayers
    )
    {
        ChannelID = channelID;
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Players = players;
        ChannelPlayers = channelPlayers;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(ChannelID);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        if (Players is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(Players, context.PooledStringManager);
        }
        if (ChannelPlayers is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(ChannelPlayers);
        }
    }

    public static PacketPlayerChannelMovedResponse Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        return new(
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadBoolean() 
                ? reader.ReadArray<PlayerMovedInitialDataWithID, PooledStringManager>(context.PooledStringManager)
                : null,
            reader.ReadBoolean()
                ? reader.ReadArray<PlayerPresenceDataWithID>()
                : null
        );
    }
}
