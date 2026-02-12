namespace MiaoNet.Shared;

public sealed class HandshakeAckData : IRefBinarySerializable<HandshakeAckData>
{
    public AuthenticationResultType AuthenticationResultType { get; }

    public byte[]? AuthenticationData { get; }

    public string? DeniedReason { get; }

    public HandshakeAckData(AuthenticationResultType authenticationResultType, byte[]? authenticationData, string? deniedReason)
    {
        AuthenticationResultType = authenticationResultType;
        AuthenticationData = authenticationData;
        DeniedReason = deniedReason;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)AuthenticationResultType);

        if (AuthenticationData is not null)
        {
            writer.Write(true);
            writer.Write((ushort)AuthenticationData.Length);
            writer.WriteSpan(AuthenticationData);
        }
        else
        {
            writer.Write(false);
        }

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
    {
        AuthenticationResultType type = (AuthenticationResultType)reader.ReadByte();
        byte[]? authData = null;
        string? deniedReason = null;
        if (reader.ReadBoolean())
        {
            ushort size = reader.ReadUInt16();
            authData = reader.ReadSpan(size).ToArray();
        }
        if (reader.ReadBoolean())
        {
            deniedReason = reader.ReadString();
        }
        return new(type, authData, deniedReason);
    }
}