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
    public void MajorAndMinorVersionsMustMatch()
    {
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(0, 4, 9), new Version(0, 5, 0)));
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(1, 5, 0), new Version(0, 5, 0)));
    }
}
