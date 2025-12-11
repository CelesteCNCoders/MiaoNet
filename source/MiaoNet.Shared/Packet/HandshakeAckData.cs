namespace MiaoNet.Shared;

public sealed class HandshakeAckData : IRefBinarySerializable<HandshakeAckData>
{
    public string? DeniedReason { get; }

    public HandshakeAckData(string? deniedReason = null)
        => DeniedReason = deniedReason;

    public void Serialize(ref RefBinaryWriter writer)
    {
        if (DeniedReason is not null)
        {
            writer.Write(true);
            writer.Write(DeniedReason);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static HandshakeAckData Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadBoolean() ? reader.ReadString() : null);
}