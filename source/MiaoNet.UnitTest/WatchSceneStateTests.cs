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

        Assert.IsNull(WatchSceneDelta.Create(1, Location, flags, flags, false));
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
            false
        );

        Assert.IsNotNull(delta);
        Assert.AreEqual(7, delta.Sequence);
        Assert.AreEqual(Location, delta.Location);
        CollectionAssert.AreEqual(new[] { "added-a", "added-z" }, delta.AddedFlags.ToArray());
        CollectionAssert.AreEqual(new[] { "removed-a", "removed-z" }, delta.RemovedFlags.ToArray());
    }

    [TestMethod]
    public void TouchSwitchAggregateProducesAnOrdinaryEntityPatch()
    {
        WatchEntityKey key = new(WatchEntityKind.TouchSwitchAndSwitchGate, 0, 1);
        Dictionary<WatchEntityKey, WatchEntityState> previous = new()
        {
            [key] = new(key, BitConverter.GetBytes(2)),
        };
        byte[] currentPayload = [.. BitConverter.GetBytes(3), .. BitConverter.GetBytes(9)];
        Dictionary<WatchEntityKey, WatchEntityState> current = new()
        {
            [key] = new(key, currentPayload),
        };

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            4,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            previous,
            current,
            [],
            forceEntityState: false,
            requiresRoomReload: false
        );

        Assert.IsNotNull(delta);
        Assert.AreEqual(WatchEntityStateMode.Patch, delta.EntityStateMode);
        Assert.AreEqual(key, delta.EntityStates.Single().Key);
        CollectionAssert.AreEqual(currentPayload, delta.EntityStates.Single().Payload.ToArray());
    }

    [TestMethod]
    public void CreateForRoomReloadIncludesCompleteStateEvenWhenUnchanged()
    {
        WatchEntityKey key = new(WatchEntityKind.TouchSwitchAndSwitchGate, 0, 1);
        Dictionary<WatchEntityKey, WatchEntityState> states = new()
        {
            [key] = new(key, BitConverter.GetBytes(3)),
        };

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            6,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            states,
            states,
            [],
            forceEntityState: false,
            requiresRoomReload: true
        );

        Assert.IsNotNull(delta);
        Assert.IsTrue(delta.RequiresRoomReload);
        Assert.AreEqual(WatchEntityStateMode.Replace, delta.EntityStateMode);
        Assert.AreEqual(key, delta.EntityStates.Single().Key);
    }

    [TestMethod]
    public void CreateForLightweightRespawnForcesCompleteStateWithoutRoomReload()
    {
        WatchEntityKey key = new(WatchEntityKind.Spring, 17);
        Dictionary<WatchEntityKey, WatchEntityState> states = new()
        {
            [key] = new(key, [1]),
        };

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            7,
            Location,
            new HashSet<string>(),
            new HashSet<string>(),
            states,
            states,
            [],
            forceEntityState: true,
            requiresRoomReload: false,
            isDeathRespawn: true
        );

        Assert.IsNotNull(delta);
        Assert.IsFalse(delta.RequiresRoomReload);
        Assert.IsTrue(delta.IsDeathRespawn);
        Assert.AreEqual(WatchEntityStateMode.Replace, delta.EntityStateMode);
        Assert.AreEqual(key, delta.EntityStates.Single().Key);
    }

    [TestMethod]
    public void OnlyProducerRoomReloadDeltaAuthorizesWatcherRoomReload()
    {
        WatchSceneDelta explicitReload = new(
            1, Location, [], [],
            requiresRoomReload: true,
            entityStateMode: WatchEntityStateMode.Replace,
            entityStates: [],
            entityEvents: []
        );
        WatchSceneDelta deathRespawn = new(
            2, Location, [], [],
            requiresRoomReload: false,
            entityStateMode: WatchEntityStateMode.Replace,
            entityStates: [],
            entityEvents: [],
            isDeathRespawn: true
        );
        WatchSceneDelta invalidPromotedDeathRespawn = new(
            3, Location, [], [],
            requiresRoomReload: true,
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
        Dictionary<WatchEntityKey, WatchEntityState> previous = new()
        {
            [oldRoomKey] = new(oldRoomKey, [1]),
        };
        Dictionary<WatchEntityKey, WatchEntityState> current = new()
        {
            [targetRoomKey] = new(targetRoomKey, [0]),
        };

        WatchSceneDelta? delta = WatchSceneDelta.Create(
            8,
            new PlayerLocation(Location.Map, "c-00"),
            new HashSet<string>(),
            new HashSet<string>(),
            previous,
            current,
            [],
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
            new Dictionary<WatchEntityKey, WatchEntityState>(),
            new Dictionary<WatchEntityKey, WatchEntityState>(),
            [],
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
        WatchSceneDelta delta = new(2, Location, ["added"], ["removed"], false);

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
            1, Location,
            new HashSet<string>(), new HashSet<string>(),
            previous, current, events,
            forceEntityState: false,
            requiresRoomReload: false
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
            previous, current, [],
            forceEntityState: false,
            requiresRoomReload: false
        );
        WatchSceneDelta? roomChanged = WatchSceneDelta.Create(
            2, Location,
            new HashSet<string>(), new HashSet<string>(),
            current, current, [],
            forceEntityState: true,
            requiresRoomReload: false
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
        Dictionary<WatchEntityKey, WatchEntityState> present = StateMap(
            key,
            (byte)WatchWingedStrawberryState.Present
        );
        Dictionary<WatchEntityKey, WatchEntityState> flyingAway = StateMap(
            key,
            (byte)WatchWingedStrawberryState.FlyingAway
        );
        Dictionary<WatchEntityKey, WatchEntityState> absent = StateMap(
            key,
            (byte)WatchWingedStrawberryState.Absent
        );

        WatchSceneDelta startFlying = CreatePatch(present, flyingAway)!;
        WatchSceneDelta finishFlying = CreatePatch(flyingAway, absent)!;

        Assert.AreEqual(WatchEntityStateMode.Patch, startFlying.EntityStateMode);
        Assert.AreEqual((byte)WatchWingedStrawberryState.FlyingAway, startFlying.EntityStates.Single().Payload.Span[0]);
        Assert.AreEqual(WatchEntityStateMode.Patch, finishFlying.EntityStateMode);
        Assert.AreEqual((byte)WatchWingedStrawberryState.Absent, finishFlying.EntityStates.Single().Payload.Span[0]);

        static Dictionary<WatchEntityKey, WatchEntityState> StateMap(WatchEntityKey key, byte value)
            => new() { [key] = new(key, [value]) };
    }

    [TestMethod]
    public void ClutterContactTombstoneDoesNotReplacePeriodicAnchors()
    {
        WatchEntityKey contactKey = new(WatchEntityKind.ClutterSystem, 0x50000001, 4);
        WatchEntityKey spinnerKey = new(WatchEntityKind.PeriodicPlatform, 12);
        byte[] inactivePayload = new byte[24];
        inactivePayload[0] = 4;
        byte[] activePayload = inactivePayload.ToArray();
        activePayload[1] = 1 << 2;
        WatchEntityState spinner = new(spinnerKey, new byte[24]);
        Dictionary<WatchEntityKey, WatchEntityState> inactive = new()
        {
            [contactKey] = new(contactKey, inactivePayload),
            [spinnerKey] = spinner,
        };
        Dictionary<WatchEntityKey, WatchEntityState> active = new()
        {
            [contactKey] = new(contactKey, activePayload),
            [spinnerKey] = spinner,
        };

        WatchSceneDelta press = CreatePatch(inactive, active)!;
        WatchSceneDelta release = CreatePatch(active, inactive)!;

        Assert.AreEqual(WatchEntityStateMode.Patch, press.EntityStateMode);
        Assert.AreEqual(contactKey, press.EntityStates.Single().Key);
        Assert.AreEqual(WatchEntityStateMode.Patch, release.EntityStateMode);
        Assert.AreEqual(contactKey, release.EntityStates.Single().Key);
    }

    [TestMethod]
    public void PeriodicAnchorProgressProducesAnOrdinaryPatch()
    {
        WatchEntityKey key = new(WatchEntityKind.PeriodicPlatform, 12);
        byte[] previousPayload = new byte[24];
        previousPayload[0] = 2;
        byte[] currentPayload = previousPayload.ToArray();
        BitConverter.GetBytes(0.25f).CopyTo(currentPayload, 12);
        Dictionary<WatchEntityKey, WatchEntityState> previous = new()
        {
            [key] = new(key, previousPayload),
        };
        Dictionary<WatchEntityKey, WatchEntityState> current = new()
        {
            [key] = new(key, currentPayload),
        };

        WatchSceneDelta delta = CreatePatch(previous, current)!;

        Assert.AreEqual(WatchEntityStateMode.Patch, delta.EntityStateMode);
        Assert.AreEqual(key, delta.EntityStates.Single().Key);
        CollectionAssert.AreEqual(currentPayload, delta.EntityStates.Single().Payload.ToArray());
    }

    private static WatchSceneDelta? CreatePatch(
        IReadOnlyDictionary<WatchEntityKey, WatchEntityState> previous,
        IReadOnlyDictionary<WatchEntityKey, WatchEntityState> current
    ) => WatchSceneDelta.Create(
        1, Location,
        new HashSet<string>(), new HashSet<string>(),
        previous, current, [],
        forceEntityState: false,
        requiresRoomReload: false
    );
}
