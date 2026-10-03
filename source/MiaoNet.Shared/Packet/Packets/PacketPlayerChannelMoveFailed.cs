namespace MiaoNet.Shared;

public sealed class PacketPlayerChannelMoveFailed : IContextlessPacket<PacketPlayerChannelMoveFailed>
{
    public enum FailedReason : byte
    {
        InvalidName
    }

    public FailedReason Reason { get; }

    public string TargetChannelName { get; }

    public PacketPlayerChannelMoveFailed(FailedReason reason, string targetChannelName)
    {
        Reason = reason;
        TargetChannelName = targetChannelName;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Reason);
        writer.Write(TargetChannelName);
    }

    public static PacketPlayerChannelMoveFailed Deserialize(ref RefBinaryReader reader)
        => new((FailedReason)reader.ReadByte(), reader.ReadString());
}
