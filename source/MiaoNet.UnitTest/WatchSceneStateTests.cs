using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchSceneStateTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/1-ForsakenCity",
        AreaMode.Normal,
        "1"
    );

    [TestMethod]
    public void CreateReturnsNullWhenSceneStateIsUnchanged()
    {
        HashSet<string> flags = ["flag-a", "flag-b"];
        HashSet<int> switches = [1, 2];

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            1,
            Location,
            flags,
            flags,
            switches,
            switches,
            false,
            false
        );

        Assert.IsNull(delta);
    }

    [TestMethod]
    public void CreateProducesDeterministicAddedAndRemovedFlags()
    {
        HashSet<string> previous = ["removed-z", "shared", "removed-a"];
        HashSet<string> current = ["added-z", "shared", "added-a"];

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            7,
            Location,
            previous,
            current,
            new HashSet<int>(),
            new HashSet<int>(),
            false,
            false
        );

        Assert.IsNotNull(delta);
        Assert.AreEqual(7, delta.Sequence);
        Assert.AreEqual(Location, delta.Location);
        CollectionAssert.AreEqual(new[] { "added-a", "added-z" }, delta.AddedFlags.ToArray());
        CollectionAssert.AreEqual(new[] { "removed-a", "removed-z" }, delta.RemovedFlags.ToArray());
        Assert.IsFalse(delta.HasTouchSwitchState);
    }

    [TestMethod]
    public void CreateProducesCompleteDeterministicTouchSwitchState()
    {
        WatchSceneDelta? delta = WatchSceneDelta.Create(
            4,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<int> { 2 },
            new HashSet<int> { 9, 3 },
            false,
            false
        );

        Assert.IsNotNull(delta);
        Assert.IsTrue(delta.HasTouchSwitchState);
        CollectionAssert.AreEqual(new[] { 3, 9 }, delta.ActiveTouchSwitchIDs.ToArray());
    }

    [TestMethod]
    public void CreateCanForceEmptyTouchSwitchStateForRoomEntry()
    {
        HashSet<int> switches = [];

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            5,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            switches,
            switches,
            true,
            false
        );

        Assert.IsNotNull(delta);
        Assert.IsTrue(delta.HasTouchSwitchState);
        Assert.IsEmpty(delta.ActiveTouchSwitchIDs);
    }

    [TestMethod]
    public void CreateForRoomReloadIncludesCompleteStateEvenWhenUnchanged()
    {
        HashSet<int> switches = [3, 9];

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            6,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            switches,
            switches,
            false,
            true
        );

        Assert.IsNotNull(delta);
        Assert.IsTrue(delta.RequiresRoomReload);
        Assert.IsTrue(delta.HasTouchSwitchState);
        CollectionAssert.AreEqual(new[] { 3, 9 }, delta.ActiveTouchSwitchIDs.ToArray());
    }

    [TestMethod]
    public void ApplyToReproducesCurrentFlags()
    {
        HashSet<string> flags = ["removed", "shared"];
        WatchSceneDelta delta = new(2, Location, ["added"], ["removed"], false, false, []);

        delta.ApplyTo(flags);

        CollectionAssert.AreEquivalent(new[] { "added", "shared" }, flags.ToArray());
    }
}
