using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchEntityCaptureCursorTests
{
    [TestMethod]
    public void AdvanceRotatesAcrossAllItems()
    {
        WatchEntityCaptureCursor cursor = new();

        Assert.AreEqual(0, cursor.GetStartIndex(5));
        cursor.Advance(2, 5);
        Assert.AreEqual(2, cursor.GetStartIndex(5));
        cursor.Advance(2, 5);
        Assert.AreEqual(4, cursor.GetStartIndex(5));
        cursor.Advance(2, 5);
        Assert.AreEqual(1, cursor.GetStartIndex(5));
    }

    [TestMethod]
    public void ResetAndCountChangesStayInRange()
    {
        WatchEntityCaptureCursor cursor = new();
        cursor.Advance(4, 5);

        Assert.AreEqual(1, cursor.GetStartIndex(3));
        cursor.Reset();
        Assert.AreEqual(0, cursor.GetStartIndex(3));
        cursor.Advance(1, 0);
        Assert.AreEqual(0, cursor.GetStartIndex(0));
    }
}
