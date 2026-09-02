using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchRemoteHeartGemAppearanceTests
{
    [TestMethod]
    public void AuthoritativeNormalHeartDiffersFromMissingState()
    {
        WatchRemoteHeartGemAppearance appearance = new();

        Assert.IsFalse(appearance.TryGet(out _));

        appearance.Apply(isGhost: false);

        Assert.IsTrue(appearance.HasState);
        Assert.IsTrue(appearance.TryGet(out bool isGhost));
        Assert.IsFalse(isGhost);
    }

    [TestMethod]
    public void NewerAuthoritativeAppearanceReplacesThePreviousValue()
    {
        WatchRemoteHeartGemAppearance appearance = new();
        appearance.Apply(isGhost: false);

        appearance.Apply(isGhost: true);

        Assert.IsTrue(appearance.TryGet(out bool isGhost));
        Assert.IsTrue(isGhost);

        appearance.Apply(isGhost: false);

        Assert.IsTrue(appearance.TryGet(out isGhost));
        Assert.IsFalse(isGhost);
    }

    [TestMethod]
    public void LiveHeartAppearanceOverridesAreaFallback()
    {
        Assert.IsFalse(WatchRemoteHeartGemAppearance.ResolveCapture(false, true));
        Assert.IsTrue(WatchRemoteHeartGemAppearance.ResolveCapture(true, false));
        Assert.IsFalse(WatchRemoteHeartGemAppearance.ResolveCapture(null, false));
        Assert.IsTrue(WatchRemoteHeartGemAppearance.ResolveCapture(null, true));
    }
}
