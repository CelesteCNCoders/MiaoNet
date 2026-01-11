using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// total size(min):
// (2 + 2) + 8 + 4 + 2 + 8 + 2 = 28 bytes
// TODO it can be smaller
public sealed class PacketPlayerFrame : IContextualPacket<PacketPlayerFrame>
{
    [Flags]
    public enum FrameFlags : ushort
    {
        None = 0,
        FacingLeft = 1 << 0, // true -> face left, false -> face right
        Dashing = 1 << 1,
        DashesChange = 1 << 2,
        HasHoldable = 1 << 3,
        StarFlying = 1 << 4,
        HasFollowerInitials = 1 << 5,
        HasFollowerDeltas = 1 << 6
    }

    #region flags

    public bool FacingLeft => Flags.HasFlag(FrameFlags.FacingLeft);

    public bool DashesChange => Flags.HasFlag(FrameFlags.DashesChange);

    public bool Dashing => Flags.HasFlag(FrameFlags.Dashing);

    public bool HasHoldable => Flags.HasFlag(FrameFlags.HasHoldable);

    public bool StarFlying => Flags.HasFlag(FrameFlags.StarFlying);

    [MemberNotNullWhen(true, nameof(FollowerInitials))]
    public bool HasFollowerInitials => Flags.HasFlag(FrameFlags.HasFollowerInitials);

    [MemberNotNullWhen(true, nameof(FollowerDeltas))]
    public bool HasFollowerDeltas => Flags.HasFlag(FrameFlags.HasFollowerDeltas);

    #endregion

    public Vector2 Position { get; }

    public PooledString Animation { get; }

    public ushort AnimationFrame { get; }

    public Vector2 Scale { get; }

    public FrameFlags Flags { get; }

    /// <summary>Included only when <see cref="DashesChange"/>.</summary>
    public byte Dashes { get; set; }

    /// <summary>Included only when <see cref="Dashing"/>.</summary>
    public byte DashDirection { get; set; }

    /// <summary>Included only when <see cref="HasHoldable"/>.</summary>
    public HoldableInfo HoldableInfo { get; set; }

    public FollowerInfo[]? FollowerInitials { get; set; }

    public FollowerInfoDelta[]? FollowerDeltas { get; set; }

    public PacketPlayerFrame(
        Vector2 position,
        PooledString animation, ushort animationFrame,
        Vector2 scale,
        FrameFlags flags
    )
    {
        Position = position;
        AnimationFrame = animationFrame;
        Animation = animation;
        Scale = scale;
        Flags = flags;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(Position);
        writer.Write(Animation, context.PooledStringManager);
        writer.Write(AnimationFrame);
        writer.Write(Scale);
        writer.Write((ushort)Flags);
        if (DashesChange)
            writer.Write(Dashes);
        if (HasHoldable)
            writer.Write(HoldableInfo, context.PooledStringManager);
        if (Dashing)
            writer.Write(DashDirection);
        if (HasFollowerInitials)
            writer.Write(FollowerInitials, context.PooledStringManager);
        else if (HasFollowerDeltas)
            writer.Write(FollowerDeltas, context.PooledStringManager);
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        var packet = new PacketPlayerFrame(
            position: reader.ReadVector2(),
            animation: reader.Read<PooledString, PooledStringManager>(context.PooledStringManager),
            animationFrame: reader.ReadUInt16(),
            scale: reader.ReadVector2(),
            flags: (FrameFlags)reader.ReadUInt16()
        );
        if (packet.DashesChange)
            packet.Dashes = reader.ReadByte();
        if (packet.HasHoldable)
            packet.HoldableInfo = reader.Read<HoldableInfo, PooledStringManager>(context.PooledStringManager);
        if (packet.Dashing)
            packet.DashDirection = reader.ReadByte();
        if (packet.HasFollowerInitials)
            packet.FollowerInitials = reader.ReadArray<FollowerInfo, PooledStringManager>(context.PooledStringManager);
        else if (packet.HasFollowerDeltas)
            packet.FollowerDeltas = reader.ReadArray<FollowerInfoDelta, PooledStringManager>(context.PooledStringManager);
        return packet;
    }
}