using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

public sealed class PlayerStateDelta : IContextualRefBinarySerializable<PlayerStateDelta, PooledStringManager>
{
    [Flags]
    public enum FrameFlags : byte
    {
        None = 0,
        DashesChange = 1 << 0,
        HasHoldable = 1 << 1,
        HasFollowerInitials = 1 << 2,
        HasFollowerDeltas = 1 << 3,
        HasWindDirection = 1 << 4,
        HasCameraPosition = 1 << 5,
    }

    #region flags

    public bool DashesChange => Flags.HasFlag(FrameFlags.DashesChange);

    public bool HasHoldable => Flags.HasFlag(FrameFlags.HasHoldable);

    [MemberNotNullWhen(true, nameof(FollowerInitials))]
    public bool HasFollowerInitials => Flags.HasFlag(FrameFlags.HasFollowerInitials);

    [MemberNotNullWhen(true, nameof(FollowerDeltas))]
    public bool HasFollowerDeltas => Flags.HasFlag(FrameFlags.HasFollowerDeltas);

    public bool HasWindDirection => Flags.HasFlag(FrameFlags.HasWindDirection);

    public bool HasCameraPosition => Flags.HasFlag(FrameFlags.HasCameraPosition);

    #endregion

    public Vector2 Position { get; }

    public PooledString Animation { get; }

    public ushort AnimationFrame { get; }

    public Vector2 Scale { get; }

    public FrameFlags Flags { get; }

    public PlayerStateFlags StateFlags { get; }

    /// <summary>Included only when <see cref="DashesChange"/>.</summary>
    public byte Dashes { get; set; }

    /// <summary>Included only when <see cref="Dashing"/>.</summary>
    public byte DashDirection { get; set; }

    /// <summary>Included only when <see cref="HasHoldable"/>.</summary>
    public HoldableInfo HoldableInfo { get; set; }

    /// <summary>Included only when <see cref="HasFollowerInitials"/>.</summary>
    public FollowerInfo[]? FollowerInitials { get; set; }

    /// <summary>Included only when <see cref="FollowerDeltas"/>.</summary>
    public FollowerInfoDelta[]? FollowerDeltas { get; set; }

    /// <summary>Included only when <see cref="HasWindDirection"/>.</summary>
    public Vector2 WindDirection { get; set; }

    /// <summary>
    /// Final world-space camera position for a Player currently being watched.
    /// </summary>
    public Vector2 CameraPosition { get; set; }

    public PlayerStateDelta(
        Vector2 position,
        PooledString animation, ushort animationFrame,
        Vector2 scale,
        FrameFlags flags,
        PlayerStateFlags stateFlags
    )
    {
        Position = position;
        AnimationFrame = animationFrame;
        Animation = animation;
        Scale = scale;
        Flags = flags;
        StateFlags = stateFlags;
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(Position);
        writer.Write(Animation, pooledStringManager);
        writer.Write(AnimationFrame);
        writer.Write(Scale);
        writer.Write((byte)Flags);
        writer.Write((byte)StateFlags);
        if (DashesChange)
            writer.Write(Dashes);
        if (HasHoldable)
            writer.Write(HoldableInfo, pooledStringManager);
        if (StateFlags.HasFlag(PlayerStateFlags.Dashing))
            writer.Write(DashDirection);
        if (HasFollowerInitials)
            writer.WriteSmall(FollowerInitials, pooledStringManager);
        else if (HasFollowerDeltas)
            writer.WriteSmall(FollowerDeltas, pooledStringManager);
        if (HasWindDirection)
            writer.Write(WindDirection);
        if (HasCameraPosition)
            writer.Write(CameraPosition);
    }

    public static PlayerStateDelta Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        Vector2 position = reader.ReadVector2();
        PooledString animation = reader.Read<PooledString, PooledStringManager>(pooledStringManager);
        ushort animationFrame = reader.ReadUInt16();
        Vector2 scale = reader.ReadVector2();
        FrameFlags flags = (FrameFlags)reader.ReadByte();
        PlayerStateFlags stateFlags = (PlayerStateFlags)reader.ReadByte();
        var packet = new PlayerStateDelta(position, animation, animationFrame, scale, flags, stateFlags);
        if (packet.DashesChange)
            packet.Dashes = reader.ReadByte();
        if (packet.HasHoldable)
            packet.HoldableInfo = reader.Read<HoldableInfo, PooledStringManager>(pooledStringManager);
        if (packet.StateFlags.HasFlag(PlayerStateFlags.Dashing))
            packet.DashDirection = reader.ReadByte();
        if (packet.HasFollowerInitials)
            packet.FollowerInitials = reader.ReadSmallArray<FollowerInfo, PooledStringManager>(pooledStringManager);
        else if (packet.HasFollowerDeltas)
            packet.FollowerDeltas = reader.ReadSmallArray<FollowerInfoDelta, PooledStringManager>(pooledStringManager);
        if (packet.HasWindDirection)
            packet.WindDirection = reader.ReadVector2();
        if (packet.HasCameraPosition)
            packet.CameraPosition = reader.ReadVector2();
        return packet;
    }
}
