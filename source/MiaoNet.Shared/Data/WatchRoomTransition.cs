namespace MiaoNet.Shared;

public readonly struct WatchRoomTransition :
    IRefBinarySerializable<WatchRoomTransition>,
    IEquatable<WatchRoomTransition>
{
    public PlayerLocation SourceLocation { get; }

    public PlayerLocation TargetLocation { get; }

    public Vector2 PlayerPosition { get; }

    public Vector2 Direction { get; }

    public WatchRoomTransition(
        PlayerLocation sourceLocation,
        PlayerLocation targetLocation,
        Vector2 playerPosition,
        Vector2 direction
    )
    {
        SourceLocation = sourceLocation;
        TargetLocation = targetLocation;
        PlayerPosition = playerPosition;
        Direction = direction;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(SourceLocation);
        writer.Write(TargetLocation);
        writer.Write(PlayerPosition);
        writer.Write(Direction);
    }

    public static WatchRoomTransition Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerLocation>(),
            reader.Read<PlayerLocation>(),
            reader.ReadVector2(),
            reader.ReadVector2()
        );

    public bool Equals(WatchRoomTransition other)
        => SourceLocation == other.SourceLocation
            && TargetLocation == other.TargetLocation
            && PlayerPosition == other.PlayerPosition
            && Direction == other.Direction;

    public override bool Equals(object? obj)
        => obj is WatchRoomTransition other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(SourceLocation, TargetLocation, PlayerPosition, Direction);

    public static bool operator ==(WatchRoomTransition left, WatchRoomTransition right)
        => left.Equals(right);

    public static bool operator !=(WatchRoomTransition left, WatchRoomTransition right)
        => !(left == right);
}
