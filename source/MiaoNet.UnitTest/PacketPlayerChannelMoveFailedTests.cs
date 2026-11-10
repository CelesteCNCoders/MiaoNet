using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class PacketPlayerChannelMoveFailedTests
{
    [TestMethod]
    public void RoundTripsReasonAndName()
    {
        var packet = new PacketPlayerChannelMoveFailed(
            PacketPlayerChannelMoveFailed.FailedReason.InvalidChannelName,
            "bad name"
        );

        var copy = RefBinarySerialization.Deserialize<PacketPlayerChannelMoveFailed>(
            RefBinarySerialization.Serialize(packet)
        );

        Assert.AreEqual(PacketPlayerChannelMoveFailed.FailedReason.InvalidChannelName, copy.Reason);
        Assert.AreEqual("bad name", copy.TargetChannelName);
    }
}
