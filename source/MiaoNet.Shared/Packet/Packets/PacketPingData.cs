using PairPlayerPing = (int playerID, int ping);

namespace MiaoNet.Shared;

public sealed class PacketPingData : IContextlessPacket<PacketPingData>
{
    public IReadOnlyCollection<PairPlayerPing> Datas { get; }

    public PacketPingData(IReadOnlyCollection<PairPlayerPing> datas)
    {
        Datas = datas;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((ushort)Datas.Count);
        foreach (var (playerID, ping) in Datas)
        {
            writer.Write(playerID);
            writer.Write(ping);
        }
    }

    public static PacketPingData Deserialize(ref RefBinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        PairPlayerPing[] datas = new PairPlayerPing[count];
        for (int i = 0; i < count; i++)
            datas[i] = (reader.ReadInt32(), reader.ReadInt32());
        return new PacketPingData(datas);
    }
}