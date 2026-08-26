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
    public void CreateForLightweightRespawnForcesCompleteStateWithoutRoomReload()
    {
        WatchEntityKey key = new(WatchEntityKind.Spring, 17);
        Dictionary<WatchEntityKey, WatchEntityState> states = new()
        {
            [key] = new(key, [1]),
        };
        HashSet<int> switches = [4];

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            7,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            switches,
            switches,
            states,
            states,
            [],
            forceTouchSwitchState: true,
            forceEntityState: true,
            requiresRoomReload: false,
            isDeathRespawn: true
        );

        Assert.IsNotNull(delta);
        Assert.IsFalse(delta.RequiresRoomReload);
        Assert.IsTrue(delta.IsDeathRespawn);
        Assert.IsTrue(delta.HasTouchSwitchState);
        Assert.AreEqual(WatchEntityStateMode.Replace, delta.EntityStateMode);
        Assert.AreEqual(key, delta.EntityStates.Single().Key);
    }

    [TestMethod]
    public void OnlyProducerRoomReloadDeltaAuthorizesWatcherRoomReload()
    {
        WatchSceneDelta explicitReload = new(
            1,
            Location,
            [],
            [],
            requiresRoomReload: true,
            hasTouchSwitchState: true,
            activeTouchSwitchIDs: [],
            entityStateMode: WatchEntityStateMode.Replace,
            entityStates: [],
            entityEvents: []
        );
        WatchSceneDelta deathRespawn = new(
            2,
            Location,
            [],
            [],
            requiresRoomReload: false,
            hasTouchSwitchState: true,
            activeTouchSwitchIDs: [],
            entityStateMode: WatchEntityStateMode.Replace,
            entityStates: [],
            entityEvents: [],
            isDeathRespawn: true
        );
        WatchSceneDelta invalidPromotedDeathRespawn = new(
            3,
            Location,
            [],
            [],
            requiresRoomReload: true,
            hasTouchSwitchState: true,
            activeTouchSwitchIDs: [],
            entityStateMode: WatchEntityStateMode.Replace,
            entityStates: [],
            entityEvents: [],
            isDeathRespawn: true
        );

        Assert.IsTrue(WatchSceneLifecyclePolicy.AuthorizesRoomReload(explicitReload));
        Assert.IsFalse(WatchSceneLifecyclePolicy.AuthorizesRoomReload(deathRespawn));
        Assert.IsFalse(WatchSceneLifecyclePolicy.AuthorizesRoomReload(invalidPromotedDeathRespawn));
    }

    [TestMethod]
    public void CreateForCrossRoomRespawnCarriesOnlyTheTargetRoomReplace()
    {
        WatchEntityKey oldRoomKey = new(WatchEntityKind.Spring, 17);
        WatchEntityKey targetRoomKey = new(WatchEntityKind.TempleCrackedBlock, 104);
        Dictionary<WatchEntityKey, WatchEntityState> previousStates = new()
        {
            [oldRoomKey] = new(oldRoomKey, [1]),
        };
        Dictionary<WatchEntityKey, WatchEntityState> targetStates = new()
        {
            [targetRoomKey] = new(targetRoomKey, [0]),
        };

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            8,
            new PlayerLocation(Location.Map, "c-00"),
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<int>(),
            new HashSet<int>(),
            previousStates,
            targetStates,
            [],
            forceTouchSwitchState: true,
            forceEntityState: true,
            requiresRoomReload: false,
            isDeathRespawn: true
        );

        Assert.IsNotNull(delta);
        Assert.IsTrue(delta.IsDeathRespawn);
        Assert.IsFalse(delta.RoomTransition.HasValue);
        Assert.AreEqual(WatchEntityStateMode.Replace, delta.EntityStateMode);
        Assert.AreEqual(targetRoomKey, delta.EntityStates.Single().Key);
        Assert.IsTrue(WatchPacketValidator.IsValid(delta));
    }

    [TestMethod]
    public void CreateCarriesAuthoritativeRoomTransitionMetadata()
    {
        PlayerLocation source = new(Location.Map, "0");
        WatchRoomTransition transition = new(
            source,
            Location,
            new Vector2(320f, 180f),
            new Vector2(0f, -1f)
        );

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            8,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<int>(),
            new HashSet<int>(),
            new Dictionary<WatchEntityKey, WatchEntityState>(),
            new Dictionary<WatchEntityKey, WatchEntityState>(),
            [],
            forceTouchSwitchState: true,
            forceEntityState: true,
            requiresRoomReload: false,
            roomTransition: transition
        );

        Assert.IsNotNull(delta);
        Assert.AreEqual(transition, delta.RoomTransition);
        Assert.AreEqual(WatchEntityStateMode.Replace, delta.EntityStateMode);
    }

    [TestMethod]
    public void ApplyToReproducesCurrentFlags()
    {
        HashSet<string> flags = ["removed", "shared"];
        WatchSceneDelta delta = new(2, Location, ["added"], ["removed"], false, false, []);

        delta.ApplyTo(flags);

        CollectionAssert.AreEquivalent(new[] { "added", "shared" }, flags.ToArray());
    }

    [TestMethod]
    public void CreateProducesDeterministicEntityPatchAndKeepsEventOrder()
    {
        WatchEntityKey key2 = new(WatchEntityKind.Spring, 2);
        WatchEntityKey key1 = new(WatchEntityKind.Spring, 1);
        Dictionary<WatchEntityKey, WatchEntityState> previous = new()
        {
            [key2] = new(key2, [1]),
        };
        Dictionary<WatchEntityKey, WatchEntityState> current = new()
        {
            [key2] = new(key2, [2]),
            [key1] = new(key1, [3]),
        };
        WatchEntityEvent[] events = [new(key2, 2, []), new(key1, 1, [])];

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            1,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<int>(),
            new HashSet<int>(),
            previous,
            current,
            events,
            false,
            false,
            false
        );

        Assert.IsNotNull(delta);
        Assert.AreEqual(WatchEntityStateMode.Patch, delta.EntityStateMode);
        CollectionAssert.AreEqual(new[] { key1, key2 }, delta.EntityStates.Select(state => state.Key).ToArray());
        CollectionAssert.AreEqual(new byte[] { 2, 1 }, delta.EntityEvents.Select(item => item.EventID).ToArray());
    }

    [TestMethod]
    public void CreateUsesCompleteEntityStateWhenKeysDisappearOrRoomChanges()
    {
        WatchEntityKey key = new(WatchEntityKind.Spring, 1);
        Dictionary<WatchEntityKey, WatchEntityState> previous = new()
        {
            [key] = new(key, [1]),
        };
        Dictionary<WatchEntityKey, WatchEntityState> current = new();

        WatchSceneDelta? removed = WatchSceneDelta.Create(
            1, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            previous, current, [],
            false, false, false
        );
        WatchSceneDelta? roomChanged = WatchSceneDelta.Create(
            2, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            current, current, [],
            true, true, false
        );

        Assert.IsNotNull(removed);
        Assert.AreEqual(WatchEntityStateMode.Replace, removed.EntityStateMode);
        Assert.IsEmpty(removed.EntityStates);
        Assert.IsNotNull(roomChanged);
        Assert.AreEqual(WatchEntityStateMode.Replace, roomChanged.EntityStateMode);
        Assert.IsEmpty(roomChanged.EntityStates);
    }

    [TestMethod]
    public void WingedStrawberryLifecycleKeepsStableKeyAndUsesPatches()
    {
        WatchEntityKey key = new(WatchEntityKind.WingedStrawberry, 17);
        Dictionary<WatchEntityKey, WatchEntityState> present = new()
        {
            [key] = new(key, [(byte)WatchWingedStrawberryState.Present]),
        };
        Dictionary<WatchEntityKey, WatchEntityState> flyingAway = new()
        {
            [key] = new(key, [(byte)WatchWingedStrawberryState.FlyingAway]),
        };
        Dictionary<WatchEntityKey, WatchEntityState> absent = new()
        {
            [key] = new(key, [(byte)WatchWingedStrawberryState.Absent]),
        };

        WatchSceneDelta? startFlying = WatchSceneDelta.Create(
            1, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            present, flyingAway, [],
            false, false, false
        );
        WatchSceneDelta? finishFlying = WatchSceneDelta.Create(
            2, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            flyingAway, absent, [],
            false, false, false
        );

        Assert.IsNotNull(startFlying);
        Assert.AreEqual(WatchEntityStateMode.Patch, startFlying.EntityStateMode);
        Assert.HasCount(1, startFlying.EntityStates);
        Assert.AreEqual(key, startFlying.EntityStates.Single().Key);
        Assert.AreEqual(
            (byte)WatchWingedStrawberryState.FlyingAway,
            startFlying.EntityStates.Single().Payload.Span[0]
        );
        Assert.IsNotNull(finishFlying);
        Assert.AreEqual(WatchEntityStateMode.Patch, finishFlying.EntityStateMode);
        Assert.HasCount(1, finishFlying.EntityStates);
        Assert.AreEqual(key, finishFlying.EntityStates.Single().Key);
        Assert.AreEqual(
            (byte)WatchWingedStrawberryState.Absent,
            finishFlying.EntityStates.Single().Payload.Span[0]
        );
    }

    [TestMethod]
    public void ClutterContactTombstoneDoesNotReplacePeriodicAnchors()
    {
        WatchEntityKey contactKey = new(WatchEntityKind.ClutterSystem, 0x50000001, 4);
        WatchEntityKey spinnerKey = new(WatchEntityKind.PeriodicPlatform, 12);
        byte[] inactiveContact = new byte[24];
        inactiveContact[0] = 4;
        byte[] activeContact = inactiveContact.ToArray();
        activeContact[1] = 1 << 2;
        WatchEntityState spinner = new(spinnerKey, new byte[24]);

        Dictionary<WatchEntityKey, WatchEntityState> inactive = new()
        {
            [contactKey] = new(contactKey, inactiveContact),
            [spinnerKey] = spinner,
        };
        Dictionary<WatchEntityKey, WatchEntityState> active = new()
        {
            [contactKey] = new(contactKey, activeContact),
            [spinnerKey] = spinner,
        };

        WatchSceneDelta? press = WatchSceneDelta.Create(
            1, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            inactive, active, [],
            false, false, false
        );
        WatchSceneDelta? release = WatchSceneDelta.Create(
            2, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            active, inactive, [],
            false, false, false
        );

        Assert.IsNotNull(press);
        Assert.AreEqual(WatchEntityStateMode.Patch, press.EntityStateMode);
        Assert.AreEqual(contactKey, press.EntityStates.Single().Key);
        Assert.IsNotNull(release);
        Assert.AreEqual(WatchEntityStateMode.Patch, release.EntityStateMode);
        Assert.AreEqual(contactKey, release.EntityStates.Single().Key);
    }

    [TestMethod]
    public void PeriodicAnchorProgressProducesAnOrdinaryPatch()
    {
        WatchEntityKey spinnerKey = new(WatchEntityKind.PeriodicPlatform, 12);
        byte[] previousPayload = new byte[24];
        previousPayload[0] = 2;
        byte[] currentPayload = previousPayload.ToArray();
        BitConverter.GetBytes(0.25f).CopyTo(currentPayload, 12);
        Dictionary<WatchEntityKey, WatchEntityState> previous = new()
        {
            [spinnerKey] = new(spinnerKey, previousPayload),
        };
        Dictionary<WatchEntityKey, WatchEntityState> current = new()
        {
            [spinnerKey] = new(spinnerKey, currentPayload),
        };

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            1, Location,
            new HashSet<string>(), new HashSet<string>(),
            new HashSet<int>(), new HashSet<int>(),
            previous, current, [],
            false, false, false
        );

        Assert.IsNotNull(delta);
        Assert.AreEqual(WatchEntityStateMode.Patch, delta.EntityStateMode);
        Assert.HasCount(1, delta.EntityStates);
        Assert.AreEqual(spinnerKey, delta.EntityStates.Single().Key);
        CollectionAssert.AreEqual(currentPayload, delta.EntityStates.Single().Payload.ToArray());
    }
}
