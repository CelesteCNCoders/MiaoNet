using MiaoNet.Shared;
using PairPlayerPing = (int playerID, int ping);

namespace MiaoNet.UnitTest;

[TestClass]
public class SmallIntEncodingTests
{
    [TestMethod]
    public void EnvelopeRequestIDIs7BitEncoded()
    {
        Assert.AreEqual(1, EnvelopeSize(PacketEnvelope.FromRequest(0)));
        Assert.AreEqual(1, EnvelopeSize(PacketEnvelope.FromRequest(127)));
        Assert.AreEqual(2, EnvelopeSize(PacketEnvelope.FromRequest(128)));
        Assert.AreEqual(2, EnvelopeSize(PacketEnvelope.FromRequest(16383)));
        Assert.AreEqual(3, EnvelopeSize(PacketEnvelope.FromRequest(16384)));
        Assert.AreEqual(5, EnvelopeSize(PacketEnvelope.FromRequest(int.MaxValue)));
    }

    [TestMethod]
    public void EnvelopeSenderPlayerIDFitsInASingleByte()
    {
        Assert.AreEqual(1, EnvelopeSize(PacketEnvelope.FromSender(7)));
        Assert.AreEqual(2, EnvelopeSize(PacketEnvelope.FromSender(300)));
    }

    [TestMethod]
    public void EnvelopeNegativeValuesStillTakeFiveBytes()
    {
        Assert.AreEqual(5, EnvelopeSize(PacketEnvelope.FromSender(-1)));
        Assert.AreEqual(5, EnvelopeSize(PacketEnvelope.FromSender(int.MinValue)));
    }

    [TestMethod]
    public void EnvelopeRoundTrips()
    {
        PacketEnvelope request = EnvelopeRoundTrip(PacketEnvelope.FromRequest(12345));
        Assert.IsTrue(request.HasRequestID);
        Assert.IsFalse(request.IsResponse);
        Assert.IsFalse(request.HasSender);
        Assert.AreEqual(12345, request.RequestID);

        PacketEnvelope response = EnvelopeRoundTrip(PacketEnvelope.ReplyTo(-1));
        Assert.IsTrue(response.HasRequestID);
        Assert.IsTrue(response.IsResponse);
        Assert.AreEqual(-1, response.RequestID);

        PacketEnvelope sender = EnvelopeRoundTrip(PacketEnvelope.FromSender(4321));
        Assert.IsTrue(sender.HasSender);
        Assert.IsFalse(sender.HasRequestID);
        Assert.AreEqual(4321, sender.SenderPlayerID);
    }

    [TestMethod]
    public void EnvelopeRejectsReadingFieldsWithoutFlag()
    {
        PacketEnvelope empty = default;
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = empty.SenderPlayerID; });
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = empty.RequestID; });

        PacketEnvelope sender = PacketEnvelope.FromSender(1);
        Assert.AreEqual(1, sender.SenderPlayerID);
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = sender.RequestID; });

        PacketEnvelope request = PacketEnvelope.FromRequest(2);
        Assert.AreEqual(2, request.RequestID);
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = request.SenderPlayerID; });
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

    private static int EnvelopeSize(PacketEnvelope envelope)
    {
        ByteArrayBufferWriter buffer = new(16);
        RefBinaryWriter writer = new(buffer);
        envelope.WriteOptional(ref writer);
        writer.Flush();
        return buffer.WrittenCount;
    }

    private static PacketEnvelope EnvelopeRoundTrip(PacketEnvelope envelope)
    {
        ByteArrayBufferWriter buffer = new(16);
        RefBinaryWriter writer = new(buffer);
        writer.Write((byte)envelope.Flags);
        envelope.WriteOptional(ref writer);
        writer.Flush();

        RefBinaryReader reader = new(buffer.WrittenSpan);
        byte flags = reader.ReadByte();
        return PacketEnvelope.ReadOptional(ref reader, (PacketEnvelopeFlags)flags);
    }

    private static T RoundTrip<T>(T value) where T : IRefBinarySerializable<T>
        => RefBinarySerialization.Deserialize<T>(RefBinarySerialization.Serialize(value));
}
