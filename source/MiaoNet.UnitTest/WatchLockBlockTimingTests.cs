using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchLockBlockTimingTests
{
    [TestMethod]
    public void RemoteUnlockPreservesVanillaKeyUseMilestones()
    {
        float[] timings =
        [
            WatchLockBlockTiming.KeyTravelDuration,
            WatchLockBlockTiming.RegisterUsedDelay,
            WatchLockBlockTiming.InsertPauseDuration,
            WatchLockBlockTiming.KeyTurnDuration,
            WatchLockBlockTiming.FinishPauseDuration,
            WatchLockBlockTiming.MinimumKeyUseDuration,
        ];

        CollectionAssert.AreEqual(new float[] { 1f, 1.2f, 0.3f, 0.3f, 0.2f, 1.8f }, timings);
        Assert.IsGreaterThan(
            timings[0],
            timings[1]
        );
        Assert.IsLessThan(
            timings[5],
            timings[1]
        );
    }
}
