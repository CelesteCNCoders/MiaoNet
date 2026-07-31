using System.Diagnostics;

namespace MiaoNet.Shared;

// client to server
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

// server to client
public sealed class PacketPlayerMapChangedNotification : PacketPlayerNotification,
    IContextualPacket<PacketPlayerMapChangedNotification>
{
    public PlayerLocation Location { get; set; }

    public PlayerState? InitialState { get; set; }

    public PacketPlayerMapChangedNotification(int playerID, PlayerLocation location)
        : base(playerID)
    {
        Location = location;
    }

    public PacketPlayerMapChangedNotification(
        int playerID, PlayerLocation location,
        PlayerState? initialState
    ) : this(playerID, location)
    {
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerID);
        writer.Write(Location);
        writer.WriteNullable(InitialState, context.PooledStringManager);
    }

    public static PacketPlayerMapChangedNotification Deserialize(
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