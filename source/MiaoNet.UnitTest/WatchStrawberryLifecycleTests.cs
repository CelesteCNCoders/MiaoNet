using Celeste.Mod.MiaoNet;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchStrawberryLifecycleTests
{
    [TestMethod]
    public void TracksWingedStrawberriesAndGoldenBerries()
    {
        Assert.IsTrue(WatchStrawberryLifecycle.IsTrackedMapEntity("strawberry", true));
        Assert.IsTrue(WatchStrawberryLifecycle.IsTrackedMapEntity("goldenBerry", false));
        Assert.IsFalse(WatchStrawberryLifecycle.IsTrackedMapEntity("strawberry", false));
        Assert.IsFalse(WatchStrawberryLifecycle.IsTrackedMapEntity("memorialTextController", true));
    }

    [TestMethod]
    public void GoldenBerryOnlyAcceptsPresentAndAbsentLifecycleStates()
    {
        Assert.IsTrue(WatchStrawberryLifecycle.IsValidState(
            "goldenBerry",
            false,
            WatchWingedStrawberryState.Present
        ));
        Assert.IsTrue(WatchStrawberryLifecycle.IsValidState(
            "goldenBerry",
            false,
            WatchWingedStrawberryState.Absent
        ));
        Assert.IsFalse(WatchStrawberryLifecycle.IsValidState(
            "goldenBerry",
            false,
            WatchWingedStrawberryState.FlyingAway
        ));
        Assert.IsTrue(WatchStrawberryLifecycle.IsValidState(
            "strawberry",
            true,
            WatchWingedStrawberryState.FlyingAway
        ));
    }

    [TestMethod]
    public void RemoteGhostAppearanceIsScopedToTheCurrentRoom()
    {
        WatchRemoteStrawberryAppearances appearances = new();
        appearances.Apply("start", [7, 11]);

        Assert.IsTrue(appearances.TryGet("start", 7, out bool firstGhost));
        Assert.IsTrue(firstGhost);
        Assert.IsTrue(appearances.TryGet("start", 9, out bool normal));
        Assert.IsFalse(normal);
        Assert.IsFalse(appearances.TryGet("next", 7, out _));

        appearances.Apply("next", []);

        Assert.IsFalse(appearances.TryGet("start", 7, out _));
        Assert.IsTrue(appearances.TryGet("next", 7, out bool nextGhost));
        Assert.IsFalse(nextGhost);
    }
}
