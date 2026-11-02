using System.Collections.Generic;

using MiaoNet.CelesteNetBridge.Protocol;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class CelesteNetRelayFilterTests
{
    [TestMethod]
    public void CelesteNetTypesAreNotRelayed()
    {
        Assert.IsFalse(CelesteNetRelayFilter.IsRelayable("playerFrame", "CelesteNet.Shared"));
        Assert.IsFalse(CelesteNetRelayFilter.IsRelayable("DataChatMessage", "CelesteNet.Client"));
    }

    [TestMethod]
    public void ModTypesAreRelayed()
    {
        Assert.IsTrue(CelesteNetRelayFilter.IsRelayable("corkr900CoopHelper_PlayerState_1_1_0", "CoopHelper"));
        Assert.IsTrue(CelesteNetRelayFilter.IsRelayable("Foxeline/TailData", "Foxeline"));
    }

    [TestMethod]
    public void UnknownTypesAreRelayedRatherThanDropped()
    {
        Assert.IsTrue(CelesteNetRelayFilter.IsRelayable("somethingNew", null));
    }

    [TestMethod]
    public void UpstreamSourceListCoversBothCelesteNetAssemblies()
    {
        CollectionAssert.AreEquivalent(
            new[] { "CelesteNet.Shared", "CelesteNet.Client" },
            new List<string>(CelesteNetRelayFilter.UpstreamSources)
        );
    }
}
