using MiaoNet.ClientShared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class BestEffortCleanupTests
{
    [TestMethod]
    public void FailingCleanup_DoesNotPreventRemainingOrFinalSteps()
    {
        List<string> calls = [];
        InvalidOperationException expected = new("component cleanup failed");

        IReadOnlyList<CleanupFailure> failures = BestEffortCleanup.Run(
            [
                new("first component", () => calls.Add("first")),
                new("failing component", () => throw expected),
                new("last component", () => calls.Add("last")),
            ],
            [new("close connection", () => calls.Add("close"))]);

        CollectionAssert.AreEqual(
            new[] { "first", "last", "close" },
            calls);
        Assert.HasCount(1, failures);
        Assert.AreEqual("failing component", failures[0].StepName);
        Assert.AreSame(expected, failures[0].Exception);
    }

    [TestMethod]
    public void FailingFinalStep_DoesNotPreventLaterFinalSteps()
    {
        List<string> calls = [];

        IReadOnlyList<CleanupFailure> failures = BestEffortCleanup.Run(
            [],
            [
                new("failing finalizer", () => throw new InvalidOperationException()),
                new("last finalizer", () => calls.Add("last")),
            ]);

        CollectionAssert.AreEqual(new[] { "last" }, calls);
        Assert.HasCount(1, failures);
        Assert.AreEqual("failing finalizer", failures[0].StepName);
    }

    [TestMethod]
    public void CleanupReentry_CannotEndTheSameConnectionTwice()
    {
        ConnectionLifecycleCoordinator lifecycle = new();
        long generation = lifecycle.Begin();
        Assert.IsTrue(lifecycle.TryEnd(generation));

        int closeCalls = 0;
        IReadOnlyList<CleanupFailure> failures = BestEffortCleanup.Run(
            [
                new("reentrant disconnect", () => Assert.IsFalse(lifecycle.TryEnd(generation))),
            ],
            [new("close connection", () => closeCalls++)]);

        Assert.IsEmpty(failures);
        Assert.AreEqual(1, closeCalls);
        Assert.AreEqual(ConnectionLifecycleState.Idle, lifecycle.State);
    }
}
