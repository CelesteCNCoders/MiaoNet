using Microsoft.VisualStudio.TestTools.UnitTesting;
using MiaoNet.CelesteNetBridge.Protocol;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class CelesteNetFrameRewriterTests {

    [TestMethod]
    public void RewriteSourceChangesOnlyTheSourceField() {
        byte[] original = CelesteNetDataTypes.PlayerInfo(7, "someone");
        byte[] rewritten = CelesteNetFrameRewriter.RewriteSource(original, 12345);

        Assert.AreEqual("playerInfo", CelesteNetTypeReader.TryReadTypeId(rewritten));
        Assert.AreNotEqual(original.Length, rewritten.Length);

        // Rewriting to the same id again must be stable: the body after the source is untouched.
        byte[] again = CelesteNetFrameRewriter.RewriteSource(rewritten, 12345);
        CollectionAssert.AreEqual(rewritten, again);
    }

    [TestMethod]
    public void RewriteSourceLeavesMalformedPayloadAlone() {
        byte[] garbage = [0x00, 0x00, 0x01, 0x02];
        CollectionAssert.AreEqual(garbage, CelesteNetFrameRewriter.RewriteSource(garbage, 9));
    }
}
