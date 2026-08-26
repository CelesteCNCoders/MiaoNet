using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchResyncCoordinatorTests
{
    private static readonly PlayerMapLocation Map = new(
        "Celeste/6-Reflection",
        AreaMode.Normal
    );

    [TestMethod]
    public void ConcurrentSessionsForOneTargetShareOneAttempt()
    {
        WatchSession first = new(1, 10, 30, Map, 1);
        WatchSession second = new(2, 20, 30, Map, 2);
        first.Activate(5);
        second.Activate(5);
        Assert.AreEqual(WatchSequenceResult.Gap, first.AcceptSequence(7));
        Assert.AreEqual(WatchSequenceResult.Gap, second.AcceptSequence(7));

        WatchResyncCoordinator coordinator = new(3);
        Assert.AreEqual(
            WatchResyncStartResult.Started,
            coordinator.TryStart(30, out WatchResyncAttempt attempt)
        );
        Assert.AreEqual(
            WatchResyncStartResult.Pending,
            coordinator.TryStart(30, out _)
        );

        Assert.IsTrue(coordinator.TryFinishAttempt(30, attempt.Generation));
        first.CompleteResync(8);
        second.CompleteResync(8);
        coordinator.Complete(30);

        Assert.AreEqual(WatchSequenceResult.Next, first.AcceptSequence(9));
        Assert.AreEqual(WatchSequenceResult.Next, second.AcceptSequence(9));
        Assert.IsFalse(coordinator.HasOperation(30));
    }

    [TestMethod]
    public void RetryGenerationRejectsLateResponsesAndStopsAtTheLimit()
    {
        WatchResyncCoordinator coordinator = new(2);
        Assert.AreEqual(
            WatchResyncStartResult.Started,
            coordinator.TryStart(30, out WatchResyncAttempt first)
        );
        Assert.IsTrue(coordinator.TryFinishAttempt(30, first.Generation));
        Assert.IsTrue(coordinator.TryScheduleRetry(30));
        Assert.AreEqual(
            WatchResyncStartResult.Pending,
            coordinator.TryStart(30, out _)
        );
        Assert.AreEqual(
            WatchResyncStartResult.Started,
            coordinator.TryStartScheduled(30, out WatchResyncAttempt second)
        );

        Assert.AreNotEqual(first.Generation, second.Generation);
        Assert.AreEqual(2, second.Number);
        Assert.IsFalse(coordinator.TryFinishAttempt(30, first.Generation));
        Assert.IsTrue(coordinator.TryFinishAttempt(30, second.Generation));
        Assert.IsTrue(coordinator.TryScheduleRetry(30));
        Assert.AreEqual(
            WatchResyncStartResult.Exhausted,
            coordinator.TryStartScheduled(30, out _)
        );

        coordinator.Complete(30);
        Assert.IsFalse(coordinator.HasOperation(30));
    }

    [TestMethod]
    public void NewOperationIgnoresResponseFromAStoppedSession()
    {
        WatchResyncCoordinator coordinator = new(3);
        Assert.AreEqual(
            WatchResyncStartResult.Started,
            coordinator.TryStart(30, out WatchResyncAttempt stopped)
        );

        coordinator.Complete(30);
        Assert.AreEqual(
            WatchResyncStartResult.Started,
            coordinator.TryStart(30, out WatchResyncAttempt current)
        );

        Assert.AreNotEqual(stopped.Generation, current.Generation);
        Assert.IsFalse(coordinator.TryFinishAttempt(30, stopped.Generation));
        Assert.IsTrue(coordinator.TryFinishAttempt(30, current.Generation));
    }
}
