#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
namespace MiaoNet.Shared;

// total size(min):
// (2 + 2) + 8 + 4 + 2 + 8 + 2 = 28 bytes
public sealed class PacketPlayerFrame : IPacket<PacketPlayerFrame>, IHasPooledString<PacketPlayerFrame>
{
    private record struct PooledStringStorage(string Animation, string? HoldableAnimation);

    [Flags]
    public enum FrameFlags : ushort
    {
        FacingLeft = 1 << 0, // true -> face left, false -> face right
        StartDash = 1 << 1,
        EndDash = 1 << 2,
        DashesChange = 1 << 3,
        HasHoldable = 1 << 4,
        StarFlying = 1 << 5
    }

    public Vector2 Position { get; }

    public PooledString Animation { get; set; }

    public ushort AnimationFrame { get; }

    public Vector2 Scale { get; }

    public FrameFlags Flags { get; }

    public byte Dashes { get; set; } // TODO readonly?

    public bool FacingLeft => Flags.HasFlag(FrameFlags.FacingLeft);

    public bool DashesChange => Flags.HasFlag(FrameFlags.DashesChange);

    public HoldableInfo HoldableInfo { get; set; }

    public bool HasHoldable => Flags.HasFlag(FrameFlags.HasHoldable);

    public PacketPlayerFrame(
        Vector2 position,
        ushort animationFrame, PooledString animationID,
        Vector2 scale,
        FrameFlags flags
    )
    {
        Position = position;
        AnimationFrame = animationFrame;
        Animation = animationID;
        Scale = scale;
        Flags = flags;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Position);
        writer.Write(AnimationFrame);
        writer.Write(Animation);
        writer.Write(Scale);
        writer.Write((ushort)Flags);
        if (DashesChange)
            writer.Write(Dashes);
        if (HasHoldable)
            writer.Write(HoldableInfo);
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader)
    {
        var packet = new PacketPlayerFrame(
            position: reader.ReadVector2(),
            animationFrame: reader.ReadUInt16(),
            animationID: reader.Read<PooledString>(),
            scale: reader.ReadVector2(),
            flags: (FrameFlags)reader.ReadUInt16()
        );
        if (packet.DashesChange)
            packet.Dashes = reader.ReadByte();
        if (packet.HasHoldable)
            packet.HoldableInfo = reader.Read<HoldableInfo>();
        return packet;
    }

    public object ResolveAllPooledString(PooledStringManager manager)
    {
        return new PooledStringStorage(
            manager.Resolve(Animation),
            HasHoldable && HoldableInfo.Type == HoldableType.Jelly
                ? manager.Resolve(HoldableInfo.Animation)
                : null
        );
    }

    public void RepackWith(object storageObject, PooledStringManager manager)
    {
        PooledStringStorage storage = (PooledStringStorage)storageObject;
        Animation = manager.Pack(storage.Animation);
        if (storage.HoldableAnimation is not null)
        {
            var hi = HoldableInfo;
            hi.Animation = manager.Pack(storage.HoldableAnimation);
            HoldableInfo = hi;
        }
    }
}