using System.Text;
using MiaoNet.CelesteNetBridge;
using MiaoNet.CelesteNetBridge.Protocol;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class CelesteNetBridgePacketTests {

    private static PacketBridgeFrame RoundTrip(PacketBridgeFrame frame) {
        ByteArrayBufferWriter buffer = new();
        var writer = new RefBinaryWriter(buffer);
        frame.Serialize(ref writer, null!);
        writer.Flush();

        var reader = new RefBinaryReader(buffer.WrittenSpan);
        return PacketBridgeFrame.Deserialize(ref reader, null!);
    }

    [TestMethod]
    public void PreservesArbitraryCelesteNetPayloadsByteForByte() {
        byte[] payload = CelesteNetDataTypes.Encode(
            0,
            "corkr900CoopHelper_BundledEntityUpdate_1_1_0",
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
        );

        PacketBridgeFrame result = RoundTrip(new PacketBridgeFrame(payload));

        CollectionAssert.AreEqual(payload, result.Payload);
    }

    [TestMethod]
    public void PreservesPayloadsUpToTheCelesteNetPacketLimit() {
        byte[] payload = new byte[CelesteNetFraming.MaxPacketSize];
        Random.Shared.NextBytes(payload);

        PacketBridgeFrame result = RoundTrip(new PacketBridgeFrame(payload));

        CollectionAssert.AreEqual(payload, result.Payload);
    }

    /// <summary>A ref struct cannot be captured by a lambda, so the assertion is spelled out.</summary>
    private static void AssertRejects(ByteArrayBufferWriter buffer) {
        var reader = new RefBinaryReader(buffer.WrittenSpan);
        try {
            PacketBridgeFrame.Deserialize(ref reader, null!);
            Assert.Fail("expected InvalidDataException");
        } catch (InvalidDataException) {
        }
    }

    [TestMethod]
    public void RejectsTruncatedPayloads() {
        ByteArrayBufferWriter buffer = new();
        var writer = new RefBinaryWriter(buffer);
        writer.Write7BitEncodedInt(64);
        writer.WriteSpan(Encoding.UTF8.GetBytes("not enough bytes"));
        writer.Flush();

        AssertRejects(buffer);
    }

    [TestMethod]
    public void RejectsEmptyOrNegativeLengths() {
        ByteArrayBufferWriter buffer = new();
        var writer = new RefBinaryWriter(buffer);
        writer.Write7BitEncodedInt(0);
        writer.Flush();

        AssertRejects(buffer);
    }
}
