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

    public Version Version { get; }

    public byte LangCode { get; }

    public string Name { get; }

    public IReadOnlyList<NetMod> NetMods { get; }

    public HandshakeData(Version version, byte langCode, string name, IReadOnlyList<NetMod> netMods)
    {
        Version = version;
        LangCode = langCode;
        Name = name;
        NetMods = netMods;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Version);
        writer.Write(LangCode);
        writer.Write(Name);
        writer.Write(NetMods);
    }

    public static HandshakeData Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadVersion(), reader.ReadByte(), reader.ReadString(), reader.ReadArray<NetMod>());
}