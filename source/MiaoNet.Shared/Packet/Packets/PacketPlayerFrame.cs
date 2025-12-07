#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
namespace MiaoNet.Shared;

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

    public Vector2 Position { get; set; }
    public ushort AnimationID { get; set; }
    public ushort AnimationFrame { get; set; }
    public Vector2 Scale { get; set; }
    public FrameFlags Flags { get; set; }
    public byte Dashes { get; set; }

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

public sealed class PacketPlayerFrameNotification : PacketPlayerNotification<PacketPlayerFrame>,
    IPacket<PacketPlayerFrameNotification>
{
    public PacketPlayerFrameNotification(int playerID, PacketPlayerFrame packet)
        : base(playerID, packet)
    {
    }

    public static PacketPlayerFrameNotification Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PacketPlayerFrame>());
}


// TODO a better naming?

// there's no non-notification version cuz the client won't send that
// it's snet by server only

/// <summary>
/// An extremely lite version of <see cref="PacketPlayerFrameNotification"/>.
/// Used to send position only, to players who are in the DebugMap.
/// </summary>
public sealed class PacketPlayerFrameNotificationLite : PacketPlayerNotification,
    IPacket<PacketPlayerFrameNotificationLite>
{
    public Vector2 Position { get; }

    public PacketPlayerFrameNotificationLite(int playerID, Vector2 position)
        : base(playerID)
    {
        Position = position;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        base.Serialize(ref writer);
        writer.Write(Position);
    }

    public static PacketPlayerFrameNotificationLite Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadVector2());
}