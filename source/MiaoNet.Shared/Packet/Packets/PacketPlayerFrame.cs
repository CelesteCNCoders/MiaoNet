namespace MiaoNet.Shared;

public sealed class PacketPlayerFrame : IPacket<PacketPlayerFrame>
{
    [Flags]
    public enum PlayerFrameActionFlags : ushort
    {
        FacingLeft = 1 << 0, // true -> face left, false -> face right
        StartDashing = 1 << 1,
        EndDashing = 1 << 2
    }

    public float X { get; set; }
    public float Y { get; set; }
    public ushort AnimationID { get; set; }
    public ushort AnimationFrame { get; set; }
    public float ScaleX { get; set; }
    public float ScaleY { get; set; }
    public PlayerFrameActionFlags Flags { get; set; }

    public bool FacingLeft => (Flags & PlayerFrameActionFlags.FacingLeft) != 0;

    public PacketPlayerFrame(
        float x, float y,
        ushort animationFrame, ushort animationID,
        float scaleX, float scaleY,
        PlayerFrameActionFlags flags
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
    }

    public static PacketPlayerFrame Deserialize(ref RefBinaryReader reader) 
        => new PacketPlayerFrame(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            (PlayerFrameActionFlags)reader.ReadUInt16()
        );
}

public sealed class PacketPlayerFrameNotify : PacketPlayerNotify<PacketPlayerFrame>, IPacket<PacketPlayerFrameNotify>
{
    public PacketPlayerFrameNotify(int playerID, PacketPlayerFrame packet)
        : base(playerID, packet)
    {
    }

    public static PacketPlayerFrameNotify Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PacketPlayerFrame>());
}