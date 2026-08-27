namespace MiaoNet.Shared;

// client to server
public sealed class PacketPlayerLocationChanged : IContextualPacket<PacketPlayerLocationChanged>
{
    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public PlayerLocation Location { get; }

    public PlayerState? InitialState { get; }

    public PacketPlayerLocationChanged(
        uint playerEpoch,
        uint playerSequence,
        PlayerLocation location,
        PlayerState? initialState
    )
    {
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write(Location);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerLocationChanged Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new PacketPlayerLocationChanged(
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.Read<PlayerLocation>(),
            reader.ReadNullable<PlayerState, PooledStringManager>(context.PooledStringManager)
        );
}

// server to client
public sealed class PacketPlayerLocationChangedNotification : PacketPlayerNotification,
    IContextualPacket<PacketPlayerLocationChangedNotification>
{
    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public PlayerLocation Location { get; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerLocationChangedNotification(
        int playerID,
        uint playerEpoch,
        uint playerSequence,
        PlayerLocation location,
        PlayerState? initialState
    )
        : base(playerID)
    {
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write(Location);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerLocationChangedNotification Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        int playerID = reader.ReadInt32();
        uint playerEpoch = reader.ReadUInt32();
        uint playerSequence = reader.ReadUInt32();
        PlayerLocation location = reader.Read<PlayerLocation>();
        PlayerState? initialState = reader.ReadNullable<PlayerState, PooledStringManager>(context.PooledStringManager);

        return new(playerID, playerEpoch, playerSequence, location, initialState);
    }
}

// server to client
public sealed class PacketPlayerLocationChangedResponse : IContextualPacket<PacketPlayerLocationChangedResponse>
{
    /// <summary>In-map players' state snapshots (ghost data).</summary>
    public IReadOnlyCollection<PlayerMovedInitialDataWithID> Players { get; }

    public PacketPlayerLocationChangedResponse(IReadOnlyCollection<PlayerMovedInitialDataWithID> players)
    {
        Players = players;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
        => writer.Write(Players, context.PooledStringManager);

    public static PacketPlayerLocationChangedResponse Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new(reader.ReadArray<PlayerMovedInitialDataWithID, PooledStringManager>(context.PooledStringManager));
}
