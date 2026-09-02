namespace MiaoNet.Shared;

public enum PlayerFrameKind : byte
{
    Delta,
    Keyframe,
}

public static class PlayerTimelineSequence
{
    public static uint Next(uint current)
    {
        unchecked
        {
            current++;
            return current == 0 ? 1 : current;
        }
    }
}

public sealed class PacketPlayerFrame : IContextualPacket<PacketPlayerFrame>
{
    public bool CanBatch => true;

    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public PlayerFrameKind Kind { get; }

    public PlayerStateDelta? StateDelta { get; }

    public PlayerState? KeyframeState { get; }

    public bool HasCameraPosition { get; }

    public Vector2 CameraPosition { get; }

    // This source is deliberately not serialized for Delta frames. The mailbox
    // clones it only when a tail replacement actually occurs, avoiding a full
    // per-frame allocation on the normal no-backlog path.
    internal PlayerState? CoalescingSourceState { get; }

    public PacketPlayerFrame(
        uint playerEpoch,
        uint playerSequence,
        PlayerStateDelta stateDelta,
        PlayerState? coalescingSourceState = null
    )
    {
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Kind = PlayerFrameKind.Delta;
        StateDelta = stateDelta;
        HasCameraPosition = stateDelta.HasCameraPosition;
        CameraPosition = stateDelta.CameraPosition;
        CoalescingSourceState = coalescingSourceState;
    }

    public PacketPlayerFrame(
        uint playerEpoch,
        uint playerSequence,
        PlayerState keyframeState,
        Vector2? cameraPosition = null
    )
    {
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Kind = PlayerFrameKind.Keyframe;
        KeyframeState = keyframeState;
        CoalescingSourceState = keyframeState;
        HasCameraPosition = cameraPosition is not null;
        CameraPosition = cameraPosition ?? Vector2.Zero;
    }

    internal PacketPlayerFrame WithCoalescingState(PlayerState state)
        => Kind == PlayerFrameKind.Keyframe
            ? this
            : new(PlayerEpoch, PlayerSequence, StateDelta!, state);

    internal PacketPlayerFrame PromoteToKeyframe()
    {
        if (Kind == PlayerFrameKind.Keyframe)
            return this;
        PlayerState state = CoalescingSourceState?.Clone()
            ?? throw new InvalidOperationException("A Delta frame cannot be coalesced without a full state snapshot.");
        return new(
            PlayerEpoch,
            PlayerSequence,
            state,
            HasCameraPosition ? CameraPosition : null
        );
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write((byte)Kind);
        if (Kind == PlayerFrameKind.Delta)
        {
            writer.Write(StateDelta!, context.PooledStringManager);
            return;
        }

        writer.Write(KeyframeState!, context.PooledStringManager);
        writer.Write(HasCameraPosition);
        if (HasCameraPosition)
            writer.Write(CameraPosition);
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        uint epoch = reader.ReadUInt32();
        uint sequence = reader.ReadUInt32();
        PlayerFrameKind kind = (PlayerFrameKind)reader.ReadByte();
        return kind switch
        {
            PlayerFrameKind.Delta => new(
                epoch,
                sequence,
                reader.Read<PlayerStateDelta, PooledStringManager>(context.PooledStringManager)
            ),
            PlayerFrameKind.Keyframe => DeserializeKeyframe(ref reader, context, epoch, sequence),
            _ => throw new InvalidDataException($"Unknown PlayerFrame kind {kind}."),
        };
    }

    private static PacketPlayerFrame DeserializeKeyframe(
        ref RefBinaryReader reader,
        IPacketSerializationContext context,
        uint epoch,
        uint sequence
    )
    {
        PlayerState state = reader.Read<PlayerState, PooledStringManager>(context.PooledStringManager);
        Vector2? camera = reader.ReadBoolean() ? reader.ReadVector2() : null;
        return new(epoch, sequence, state, camera);
    }
}
