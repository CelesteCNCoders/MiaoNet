using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class ConnectionVersionTests
{
    [TestMethod]
    public void PatchVersionsAreCompatible()
    {
        Assert.IsTrue(Connection.IsVersionCompatible(new Version(0, 5, 0), new Version(0, 5, 1)));
        Assert.IsTrue(Connection.IsVersionCompatible(new Version(0, 5, 99), new Version(0, 5, 0)));
    }

    [TestMethod]
    public void MajorMustMatch()
    {
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(1, 4, 9), new Version(0, 4, 9)));
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(1, 5, 0), new Version(0, 5, 0)));
    }

    [TestMethod]
    public void MinorVersionsMustMatchWhileMajorZero()
    {
        Assert.IsTrue(Connection.IsVersionCompatible(new Version(0, 4, 9), new Version(0, 4, 8)));
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(0, 4, 9), new Version(0, 5, 9)));
    }

    [TestMethod]
    public void MinorVersionBackwardsCompatibility()
    {
        Assert.IsTrue(Connection.IsVersionCompatible(new Version(1, 4, 9), new Version(1, 5, 9)));
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(1, 5, 9), new Version(1, 4, 0)));
    }
}
