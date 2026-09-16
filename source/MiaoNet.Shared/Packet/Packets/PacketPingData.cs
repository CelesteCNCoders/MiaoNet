using PairPlayerPing = (int playerID, int ping);

namespace MiaoNet.Shared;

public sealed class PacketPingData : IContextlessPacket<PacketPingData>
{
    public IReadOnlyCollection<PairPlayerPing> Data { get; }

    public PacketPingData(IReadOnlyCollection<PairPlayerPing> data)
    {
        Data = data;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((ushort)Data.Count);
        foreach (var (playerID, ping) in Data)
        {
            writer.Write7BitEncodedInt(playerID);
            writer.Write7BitEncodedInt(ping);
        }
    }

    public static PacketPingData Deserialize(ref RefBinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        PairPlayerPing[] data = new PairPlayerPing[count];
        for (int i = 0; i < count; i++)
            data[i] = (reader.Read7BitEncodedInt(), reader.Read7BitEncodedInt());
        return new PacketPingData(data);
    }
}
