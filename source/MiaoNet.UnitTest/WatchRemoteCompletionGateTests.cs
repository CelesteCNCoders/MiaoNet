using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchRemoteCompletionGateTests
{
    [TestMethod]
    public void DefersAuthoritativeRemovalUntilVisualCompletes()
    {
        WatchRemoteCompletionGate gate = new();

        Assert.IsFalse(gate.MarkAuthoritativeGone());
        Assert.IsTrue(gate.AuthoritativeGone);
        Assert.IsFalse(gate.VisualComplete);
        Assert.IsTrue(gate.MarkVisualComplete());
    }

    [TestMethod]
    public void WaitsForAuthoritativeRemovalAfterVisualCompletes()
    {
        WatchRemoteCompletionGate gate = new();

        Assert.IsFalse(gate.MarkVisualComplete());
        Assert.IsFalse(gate.AuthoritativeGone);
        Assert.IsTrue(gate.VisualComplete);
        Assert.IsTrue(gate.MarkAuthoritativeGone());
    }

    [TestMethod]
    public void ResetStartsANewRemotePresentation()
    {
        WatchRemoteCompletionGate gate = new();
        gate.MarkAuthoritativeGone();
        gate.MarkVisualComplete();

        gate.Reset();

        Assert.IsFalse(gate.AuthoritativeGone);
        Assert.IsFalse(gate.VisualComplete);
    }

    [TestMethod]
    public void RepeatedAuthoritativeStateDoesNotCompleteTheVisualEarly()
    {
        WatchRemoteCompletionGate gate = new();

        Assert.IsFalse(gate.MarkAuthoritativeGone());
        Assert.IsFalse(gate.MarkAuthoritativeGone());
        Assert.IsTrue(gate.MarkVisualComplete());
    }
}
