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

    public float X { get; set; }
    public float Y { get; set; }
    public ushort AnimationID { get; set; }
    public ushort AnimationFrame { get; set; }
    public float ScaleX { get; set; }
    public float ScaleY { get; set; }
    public FrameFlags Flags { get; set; }
    public byte Dashes { get; set; }

    public bool FacingLeft => Flags.HasFlag(FrameFlags.FacingLeft);

    public bool DashesChange => Flags.HasFlag(FrameFlags.DashesChange);

    public PacketPlayerFrame(
        float x, float y,
        ushort animationFrame, ushort animationID,
        float scaleX, float scaleY,
        FrameFlags flags
    )
    {
        X = x;
        Y = y;
        AnimationFrame = animationFrame;
        AnimationID = animationID;
        ScaleX = scaleX;
        ScaleY = scaleY;
        Flags = flags;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(AnimationFrame);
        writer.Write(AnimationID);
        writer.Write(ScaleX);
        writer.Write(ScaleY);
        writer.Write((ushort)Flags);
        if (DashesChange)
            writer.Write(Dashes);
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader)
    {
        var packet = new PacketPlayerFrame(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            (FrameFlags)reader.ReadUInt16()
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