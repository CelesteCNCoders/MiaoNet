using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchAnimationSelectionTests
{
    [TestMethod]
    public void RequestedAnimationIsUsedWhenAvailable()
    {
        string? selected = WatchAnimationSelection.Select(
            "getHit",
            "idle",
            id => id is "idle" or "getHit"
        );

        Assert.AreEqual("getHit", selected);
    }

    [TestMethod]
    public void MissingAnimationKeepsCurrentAnimation()
    {
        string? selected = WatchAnimationSelection.Select(
            "getHit",
            "laugh",
            id => id is "idle" or "laugh"
        );

        Assert.AreEqual("laugh", selected);
    }

    [TestMethod]
    public void MissingCurrentAnimationFallsBackToIdle()
    {
        string? selected = WatchAnimationSelection.Select(
            "attack1Begin",
            "missingCurrent",
            id => id == "idle"
        );

        Assert.AreEqual("idle", selected);
    }

    [TestMethod]
    public void MissingAnimationsDoNotRequestPlayback()
    {
        string? selected = WatchAnimationSelection.Select(
            "attack1Recoil",
            null,
            _ => false
        );

        Assert.IsNull(selected);
    }
}
