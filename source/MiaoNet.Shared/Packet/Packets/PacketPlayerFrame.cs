#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
namespace MiaoNet.Shared;

// total size:
// (2 + 2) + 8 + 2 + 2 + 8 + 2 = 26 bytes
public sealed class PacketPlayerFrame : IPacket<PacketPlayerFrame>
{
    [Flags]
    public enum FrameFlags : ushort
    {
        FacingLeft = 1 << 0, // true -> face left, false -> face right
        StartDash = 1 << 1,
        EndDash = 1 << 2,
        DashesChange = 1 << 3
    }

    public Vector2 Position { get; }

    public ushort AnimationID { get; }

    public ushort AnimationFrame { get; }

    public Vector2 Scale { get; }

    public FrameFlags Flags { get; }

    public byte Dashes { get; set; } // TODO readonly?

    public bool FacingLeft => Flags.HasFlag(FrameFlags.FacingLeft);

    public bool DashesChange => Flags.HasFlag(FrameFlags.DashesChange);

    public PacketPlayerFrame(
        Vector2 position,
        ushort animationFrame, ushort animationID,
        Vector2 scale,
        FrameFlags flags
    )
    {
        Position = position;
        AnimationFrame = animationFrame;
        AnimationID = animationID;
        Scale = scale;
        Flags = flags;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Position);
        writer.Write(AnimationFrame);
        writer.Write(AnimationID);
        writer.Write(Scale);
        writer.Write((ushort)Flags);
        if (DashesChange)
            writer.Write(Dashes);
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader)
    {
        var packet = new PacketPlayerFrame(
            position: reader.ReadVector2(),
            animationFrame: reader.ReadUInt16(),
            animationID: reader.ReadUInt16(),
            scale: reader.ReadVector2(),
            flags: (FrameFlags)reader.ReadUInt16()
        );
        if (packet.DashesChange)
            packet.Dashes = reader.ReadByte();
        return packet;
    }
}