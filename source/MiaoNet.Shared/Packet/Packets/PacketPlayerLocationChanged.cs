namespace MiaoNet.Shared;

// client to server
public sealed class PacketPlayerLocationChanged : IContextualPacket<PacketPlayerLocationChanged>
{
    public PlayerLocation Location { get; }

    public PlayerState? InitialState { get; }

    public PacketPlayerLocationChanged(PlayerLocation location, PlayerState? initialState)
    {
        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(Location);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerLocationChanged Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
        => new PacketPlayerLocationChanged(
            reader.Read<PlayerLocation>(),
            reader.ReadNullable<PlayerState, PooledStringManager>(context.PooledStringManager)
        );
}

// server to client
public sealed class PacketPlayerLocationChangedNotification : PacketPlayerNotification,
    IContextualPacket<PacketPlayerLocationChangedNotification>
{
    public PlayerLocation Location { get; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerLocationChangedNotification(int playerID, PlayerLocation location, PlayerState? initialState)
        : base(playerID)
    {
        Location = location;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(Location);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerLocationChangedNotification Deserialize(
        ref RefBinaryReader reader,
        IPacketSerializationContext context
    )
    {
        int playerID = reader.ReadInt32();
        PlayerLocation location = reader.Read<PlayerLocation>();
        PlayerState? initialState = reader.ReadNullable<PlayerState, PooledStringManager>(context.PooledStringManager);

        return new(playerID, location, initialState);
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
