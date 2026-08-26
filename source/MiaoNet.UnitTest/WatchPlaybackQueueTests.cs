using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchPlaybackQueueTests
{
    [TestMethod]
    public void EntryBecomesDueAtExactPlaybackBoundary()
    {
        WatchPlaybackQueue<string> queue = new(4);

        Assert.AreEqual(WatchPlaybackEnqueueResult.Success, queue.Enqueue(100, "frame"));
        Assert.IsFalse(queue.TryDequeueDue(99, out _));
        Assert.IsTrue(queue.TryDequeueDue(100, out WatchPlaybackEntry<string> entry));
        Assert.AreEqual(100, entry.ReceivedAt);
        Assert.AreEqual("frame", entry.Value);
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void EqualTimestampsPreserveInsertionOrder()
    {
        WatchPlaybackQueue<int> queue = new(4);

        Assert.AreEqual(WatchPlaybackEnqueueResult.Success, queue.Enqueue(100, 1));
        Assert.AreEqual(WatchPlaybackEnqueueResult.Success, queue.Enqueue(100, 2));
        Assert.IsTrue(queue.TryDequeueDue(100, out WatchPlaybackEntry<int> first));
        Assert.IsTrue(queue.TryDequeueDue(100, out WatchPlaybackEntry<int> second));
        Assert.AreEqual(1, first.Value);
        Assert.AreEqual(2, second.Value);
    }

    [TestMethod]
    public void PeekKeepsFutureEntryAvailableForInterpolation()
    {
        WatchPlaybackQueue<int> queue = new(4);
        queue.Enqueue(100, 1);
        queue.Enqueue(200, 2);

        Assert.IsTrue(queue.TryDequeueDue(150, out WatchPlaybackEntry<int> current));
        Assert.AreEqual(1, current.Value);
        Assert.IsTrue(queue.TryPeek(out WatchPlaybackEntry<int> future));
        Assert.AreEqual(200, future.ReceivedAt);
        Assert.AreEqual(2, future.Value);
    }

    [TestMethod]
    public void CapacityAndTimestampRegressionAreReportedWithoutMutation()
    {
        WatchPlaybackQueue<int> queue = new(2);
        Assert.AreEqual(WatchPlaybackEnqueueResult.Success, queue.Enqueue(100, 1));
        Assert.AreEqual(WatchPlaybackEnqueueResult.TimestampRegression, queue.Enqueue(99, 2));
        Assert.AreEqual(1, queue.Count);
        Assert.AreEqual(WatchPlaybackEnqueueResult.Success, queue.Enqueue(100, 3));
        Assert.AreEqual(WatchPlaybackEnqueueResult.CapacityExceeded, queue.Enqueue(101, 4));
        Assert.AreEqual(2, queue.Count);
    }

    [TestMethod]
    public void ClearResetsContentsAndTimestampBoundary()
    {
        WatchPlaybackQueue<int> queue = new(2);
        queue.Enqueue(100, 1);

        queue.Clear();

        Assert.AreEqual(0, queue.Count);
        Assert.IsFalse(queue.TryPeek(out _));
        Assert.AreEqual(WatchPlaybackEnqueueResult.Success, queue.Enqueue(50, 2));
    }

    [TestMethod]
    public void InterpolationAmountUsesPlaybackClockAndClamps()
    {
        Assert.AreEqual(0f, WatchPlaybackTiming.GetInterpolationAmount(100, 200, 50));
        Assert.AreEqual(0.5f, WatchPlaybackTiming.GetInterpolationAmount(100, 200, 150));
        Assert.AreEqual(1f, WatchPlaybackTiming.GetInterpolationAmount(100, 200, 250));
        Assert.AreEqual(1f, WatchPlaybackTiming.GetInterpolationAmount(100, 100, 100));
    }

    [TestMethod]
    public void PlaybackDelayIsExactlyFifteenNominalFrames()
    {
        Assert.AreEqual(15L, WatchPlaybackTiming.GetDelayTicks(60));
        Assert.AreEqual(2_500_000L, WatchPlaybackTiming.GetDelayTicks(10_000_000));
    }
}
