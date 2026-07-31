namespace MiaoNet.Shared;

public sealed class HandshakeData : IRefBinarySerializable<HandshakeData>
{
    public sealed class NetMod : IRefBinarySerializable<NetMod>
    {
        public Version Version { get; set; }

        public string Name { get; set; }

        public NetMod(Version version, string name)
        {
            Version = version;
            Name = name;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(Version);
            writer.Write(Name);
        }

        public static NetMod Deserialize(ref RefBinaryReader reader)
            => new(reader.ReadVersion(), reader.ReadString());
    }

    public LanguageCode LanguageCode { get; }

    public bool IsAuthorize { get; }

    public byte[] AuthenticationData { get; }

    public IReadOnlyList<NetMod> NetMods { get; }

    public HandshakeData(LanguageCode languageCode, bool isAuthorize, byte[] authenticationData, IReadOnlyList<NetMod> netMods)
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
        NetMod[] netMods = reader.ReadArray<NetMod>();
        return new HandshakeData(LanguageCode, isAuthorize, authData, netMods);
    }
}