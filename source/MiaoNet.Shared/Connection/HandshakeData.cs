namespace MiaoNet.Shared;

public sealed class HandshakeData : IRefBinarySerializable<HandshakeData>
{
    public LanguageCode LanguageCode { get; }

    public bool IsAuthorize { get; }

    public byte[] AuthenticationData { get; }

    public IReadOnlyList<NetModInfo> NetMods { get; }

    public HandshakeData(LanguageCode languageCode, bool isAuthorize, byte[] authenticationData, IReadOnlyList<NetModInfo> netMods)
    {
        LanguageCode = languageCode;
        IsAuthorize = isAuthorize;
        AuthenticationData = authenticationData;
        NetMods = netMods;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)LanguageCode);
        writer.Write(IsAuthorize);
        writer.Write((ushort)AuthenticationData.Length);
        writer.WriteSpan(AuthenticationData);
        writer.Write(NetMods);
    }

    public static HandshakeData Deserialize(ref RefBinaryReader reader)
    {
        LanguageCode LanguageCode = (LanguageCode)reader.ReadByte();
        bool isAuthorize = reader.ReadBoolean();
        ushort authDataLength = reader.ReadUInt16();
        byte[] authData = reader.ReadSpan(authDataLength).ToArray();
        NetModInfo[] netMods = reader.ReadArray<NetModInfo>();
        return new HandshakeData(LanguageCode, isAuthorize, authData, netMods);
    }
}
