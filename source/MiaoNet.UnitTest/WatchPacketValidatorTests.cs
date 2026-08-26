using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchPacketValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/1-ForsakenCity",
        AreaMode.Normal,
        "1"
    );

    [TestMethod]
    public void ValidSnapshotAndDeltaAreAccepted()
    {
        WatchSceneSnapshot snapshot = new(Location, 0, ["flag-a", "flag-b"], [3, 8]);
        WatchSceneDelta delta = new(1, Location, ["flag-c"], ["flag-a"], false, true, [3, 8]);

        Assert.IsTrue(WatchPacketValidator.IsValid(snapshot));
        Assert.IsTrue(WatchPacketValidator.IsValid(delta));
    }

    [TestMethod]
    public void DuplicateOrOverlappingFlagsAreRejected()
    {
        WatchSceneSnapshot duplicateSnapshot = new(Location, 0, ["flag", "flag"], []);
        WatchSceneDelta duplicateDelta = new(1, Location, ["flag", "flag"], [], false, false, []);
        WatchSceneDelta overlappingDelta = new(1, Location, ["flag"], ["flag"], false, false, []);

        Assert.IsFalse(WatchPacketValidator.IsValid(duplicateSnapshot));
        Assert.IsFalse(WatchPacketValidator.IsValid(duplicateDelta));
        Assert.IsFalse(WatchPacketValidator.IsValid(overlappingDelta));
    }

    [TestMethod]
    public void FlagCountBoundaryIsEnforced()
    {
        string[] boundary = Enumerable.Range(0, WatchPacketValidator.MaxFlagCount)
            .Select(index => $"flag-{index}")
            .ToArray();
        string[] tooMany = [.. boundary, "extra"];

        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, boundary, [], false, false, [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, tooMany, [], false, false, [])));
    }

    [TestMethod]
    public void FlagUtf8LengthBoundaryIsEnforced()
    {
        string boundary = new('a', WatchPacketValidator.MaxFlagUtf8Bytes);
        string tooLong = new('a', WatchPacketValidator.MaxFlagUtf8Bytes + 1);

        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [boundary], [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [tooLong], [])));
    }

    [TestMethod]
    public void TouchSwitchIDsMustBeUniqueAndNonNegative()
    {
        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [0, 7])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [7, 7])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, [], [], false, true, [-1])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, [], [], false, false, [1])));
    }

    [TestMethod]
    public void RoomReloadRequiresCompleteTouchSwitchState()
    {
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneDelta(1, Location, [], [], true, true, [])
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(1, Location, [], [], true, false, [])
        ));
    }

    [TestMethod]
    public void SequenceBoundaryIsEnforced()
    {
        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, -1, [], [])));
        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, ["flag"], [], false, false, [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, [], [], false, false, [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(0, Location, [], [], false, false, [])));
    }

    [TestMethod]
    public void SceneStateMustBelongToAMap()
    {
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(PlayerLocation.Empty, 0, [], [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(1, PlayerLocation.Empty, [], [], false, false, [])
        ));
    }

    [TestMethod]
    public void PacketPayloadBoundaryIsEnforced()
    {
        string[] largeState = Enumerable.Range(0, 31)
            .Select(index => $"{index}-" + new string('a', 998))
            .ToArray();

        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, largeState, [])));
    }
}
