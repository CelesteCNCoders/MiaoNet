namespace Celeste.Mod.CelesteNet.DataTypes;

public sealed class DataConnectionInfo : DataType<DataConnectionInfo>
{
    static DataConnectionInfo() => DataID = "conInfo";

    public override DataFlags DataFlags => DataFlags.CoreType;

    public DataPlayerInfo? Player;
    public int TCPPingMs;
    public int? UDPPingMs;

    public override MetaType[] GenerateMeta(DataContext context)
        => [new MetaPlayerPublicState(Player), new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true)];

    public override void FixupMeta(DataContext context)
    {
        Player = Get<MetaPlayerPublicState>(context);
        Get<MetaBoundRef>(context).ID = Player?.ID ?? uint.MaxValue;
    }

    protected override void Read(CelesteNetBinaryReader reader)
    {
        TCPPingMs = reader.Read7BitEncodedInt();
        UDPPingMs = reader.ReadBoolean() ? reader.Read7BitEncodedInt() : null;
    }

    protected override void Write(CelesteNetBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(TCPPingMs);
        writer.Write(UDPPingMs.HasValue);
        if (UDPPingMs.HasValue)
            writer.Write7BitEncodedInt(UDPPingMs.Value);
    }
}
