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

    public byte LangCode { get; }

    public AuthenticationType Type { get; }

    public byte[] AuthenticationData { get; }

    public IReadOnlyList<NetMod> NetMods { get; }

    public HandshakeData(byte langCode, AuthenticationType type, byte[] authenticationData, IReadOnlyList<NetMod> netMods)
    {
        LangCode = langCode;
        Type = type;
        AuthenticationData = authenticationData;
        NetMods = netMods;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(LangCode);
        writer.Write((byte)Type);
        writer.Write((ushort)AuthenticationData.Length);
        writer.WriteSpan(AuthenticationData);
        writer.Write(NetMods);
    }

    public static HandshakeData Deserialize(ref RefBinaryReader reader)
    {
        byte langCode = reader.ReadByte();
        AuthenticationType type = (AuthenticationType)reader.ReadByte();
        ushort authDataLength = reader.ReadUInt16();
        byte[] authData = reader.ReadSpan(authDataLength).ToArray();
        NetMod[] netMods = reader.ReadArray<NetMod>();
        return new HandshakeData(langCode, type, authData, netMods);
    }
}