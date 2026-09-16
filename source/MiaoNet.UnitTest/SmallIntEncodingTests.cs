using MiaoNet.Shared;
using PairPlayerPing = (int playerID, int ping);

namespace MiaoNet.UnitTest;

[TestClass]
public class SmallIntEncodingTests
{
    [TestMethod]
    public void RequestIDIs7BitEncoded()
    {
        Assert.AreEqual(1, PayloadSize(new PacketPing { RequestID = 0 }));
        Assert.AreEqual(1, PayloadSize(new PacketPing { RequestID = 127 }));
        Assert.AreEqual(2, PayloadSize(new PacketPing { RequestID = 128 }));
        Assert.AreEqual(2, PayloadSize(new PacketPing { RequestID = 16383 }));
        Assert.AreEqual(3, PayloadSize(new PacketPing { RequestID = 16384 }));
        Assert.AreEqual(5, PayloadSize(new PacketPing { RequestID = int.MaxValue }));
    }

    [TestMethod]
    public void SmallPlayerIDFitsInASingleByte()
    {
        Assert.AreEqual(1, PayloadSize(new PacketPlayerLeft(7)));
        Assert.AreEqual(2, PayloadSize(new PacketPlayerLeft(300)));
    }

    [TestMethod]
    public void NegativeValuesStillTakeFiveBytes()
    {
        Assert.AreEqual(5, PayloadSize(new PacketPlayerLeft(-1)));
        Assert.AreEqual(5, PayloadSize(new PacketPlayerLeft(int.MinValue)));
    }

    [TestMethod]
    public void PlayerLeftRoundTrips()
    {
        Assert.AreEqual(12345, RoundTrip(new PacketPlayerLeft(12345)).PlayerID);
        Assert.AreEqual(-1, RoundTrip(new PacketPlayerLeft(-1)).PlayerID);
    }

    [TestMethod]
    public void PlayerJoinedRoundTrips()
    {
        PacketPlayerJoined packet = RoundTrip(new PacketPlayerJoined(
            channelID: 2,
            playerID: 4321,
            new PlayerInfo(1, "name", "prefix", "avatar", Color.White)
        ));

        Assert.AreEqual(2, packet.ChannelID);
        Assert.AreEqual(4321, packet.PlayerID);
        Assert.AreEqual("name", packet.PlayerInfo.Name);
    }

    [TestMethod]
    public void PingDataRoundTrips()
    {
        PacketPingData packet = RoundTrip(new PacketPingData(new PairPlayerPing[]
        {
            (1, 20),
            (300, 400),
        }));

        Assert.HasCount(2, packet.Data);
        Assert.AreEqual((1, 20), packet.Data.ElementAt(0));
        Assert.AreEqual((300, 400), packet.Data.ElementAt(1));
    }

    private static int PayloadSize<T>(T packet) where T : IRefBinarySerializable
        => RefBinarySerialization.Serialize(packet).Length;

    private static T RoundTrip<T>(T value) where T : IRefBinarySerializable<T>
        => RefBinarySerialization.Deserialize<T>(RefBinarySerialization.Serialize(value));
}
