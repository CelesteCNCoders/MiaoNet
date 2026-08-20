using MiaoNet.ClientShared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class ConnectionLifecycleCoordinatorTests
{
    [TestMethod]
    public void BeginAndConnect_RequireTheCurrentGeneration()
    {
        ConnectionLifecycleCoordinator lifecycle = new();

        long generation = lifecycle.Begin();

        Assert.AreEqual(ConnectionLifecycleState.Connecting, lifecycle.State);
        Assert.IsTrue(lifecycle.IsCurrent(generation));
        Assert.IsFalse(lifecycle.TryMarkConnected(generation + 1));
        Assert.IsTrue(lifecycle.TryMarkConnected(generation));
        Assert.AreEqual(ConnectionLifecycleState.Connected, lifecycle.State);
        Assert.IsFalse(lifecycle.TryMarkConnected(generation));
    }

    [TestMethod]
    public void EndingAnOperation_IsIdempotent()
    {
        ConnectionLifecycleCoordinator lifecycle = new();
        long generation = lifecycle.Begin();

        Assert.IsTrue(lifecycle.TryEnd(generation));
        Assert.IsFalse(lifecycle.TryEnd(generation));
        Assert.AreEqual(ConnectionLifecycleState.Idle, lifecycle.State);
    }

    [TestMethod]
    public void StaleCallbacks_CannotAffectAReplacementOperation()
    {
        ConnectionLifecycleCoordinator lifecycle = new();
        long oldGeneration = lifecycle.Begin();
        Assert.IsTrue(lifecycle.TryEnd(oldGeneration));

        long newGeneration = lifecycle.Begin();

        Assert.AreNotEqual(oldGeneration, newGeneration);
        Assert.IsFalse(lifecycle.IsCurrent(oldGeneration));
        Assert.IsFalse(lifecycle.TryMarkConnected(oldGeneration));
        Assert.IsFalse(lifecycle.TryEnd(oldGeneration));
        Assert.IsTrue(lifecycle.IsCurrent(newGeneration));
        Assert.AreEqual(ConnectionLifecycleState.Connecting, lifecycle.State);
    }

    [TestMethod]
    public void Begin_RejectsOverlappingOperations()
    {
        ConnectionLifecycleCoordinator lifecycle = new();
        lifecycle.Begin();

        Assert.ThrowsExactly<InvalidOperationException>(() => lifecycle.Begin());
    }
}
