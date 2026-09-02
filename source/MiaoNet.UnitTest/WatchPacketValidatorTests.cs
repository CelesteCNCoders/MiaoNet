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
        WatchEntityKey key = new(WatchEntityKind.Spring, 4);
        WatchSceneSnapshot snapshot = new(
            Location,
            0,
            ["flag-a", "flag-b"],
            [new(key, [1])]
        );
        WatchSceneDelta delta = new(
            1,
            Location,
            ["flag-c"],
            ["flag-a"],
            false,
            WatchEntityStateMode.Patch,
            [new(key, [0])],
            [new(key, 1, [])]
        );

        Assert.IsTrue(WatchPacketValidator.IsValid(snapshot));
        Assert.IsTrue(WatchPacketValidator.IsValid(delta));
    }

    [TestMethod]
    public void DuplicateOrOverlappingFlagsAreRejected()
    {
        WatchSceneSnapshot duplicateSnapshot = new(Location, 0, ["flag", "flag"]);
        WatchSceneDelta duplicateDelta = new(1, Location, ["flag", "flag"], [], false);
        WatchSceneDelta overlappingDelta = new(1, Location, ["flag"], ["flag"], false);

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

        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, boundary, [], false)));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, tooMany, [], false)));
    }

    [TestMethod]
    public void FlagUtf8LengthBoundaryIsEnforced()
    {
        string boundary = new('a', WatchPacketValidator.MaxFlagUtf8Bytes);
        string tooLong = new('a', WatchPacketValidator.MaxFlagUtf8Bytes + 1);

        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [boundary])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [tooLong])));
    }

    [TestMethod]
    public void TouchSwitchAggregateIDsMustBeSortedUniqueAndNonNegative()
    {
        WatchEntityKey key = new(WatchEntityKind.TouchSwitchAndSwitchGate, 0, 1);

        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchEntityState(key, [])));
        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchEntityState(
            key,
            [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(7)]
        )));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchEntityState(
            key,
            [.. BitConverter.GetBytes(7), .. BitConverter.GetBytes(7)]
        )));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchEntityState(
            key,
            BitConverter.GetBytes(-1)
        )));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchEntityState(
            new(WatchEntityKind.TouchSwitchAndSwitchGate, 1, 1),
            []
        )));
    }

    [TestMethod]
    public void RoomReloadRequiresCompleteEntityState()
    {
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneDelta(1, Location, [], [], true)
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], true,
                WatchEntityStateMode.None, [], []
            )
        ));
    }

    [TestMethod]
    public void LifecycleReplaceMetadataIsValidated()
    {
        PlayerLocation source = new(Location.Map, "0");
        WatchRoomTransition transition = new(
            source,
            Location,
            new Vector2(320f, 180f),
            new Vector2(0f, 1f)
        );

        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.Replace, [], [], true
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.None, [], [], true
            )
        ));
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.Replace, [], [], false, transition
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.Replace, [], [], false,
                new WatchRoomTransition(source, Location, Vector2.Zero, Vector2.Zero)
            )
        ));
    }

    [TestMethod]
    public void EntityKeysAndPayloadsAreValidated()
    {
        WatchEntityKey validKey = new(WatchEntityKind.Spring, 7);
        WatchEntityState validState = new(validKey, [1]);

        Assert.IsTrue(WatchPacketValidator.IsValid(validState));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchEntityState(validKey, [])));

        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], [validState])
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], [validState, validState])
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new((WatchEntityKind)ushort.MaxValue, 7), [])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], [new(new(WatchEntityKind.Spring, -1), [])])
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(validKey, new byte[WatchPacketValidator.MaxEntityPayloadBytes + 1])]
            )
        ));
    }

    [TestMethod]
    public void EntityStateModesAndEventsAreValidated()
    {
        WatchEntityKey key = new(WatchEntityKind.Spring, 7);
        WatchEntityState state = new(key, []);

        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchEntityEvent(key, 1, [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchEntityEvent(key, 0, [])));

        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.None, [state], []
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.Patch, [], []
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], true,
                WatchEntityStateMode.Patch, [state], []
            )
        ));
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.None, [], [new(key, 1, [])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(
                1, Location, [], [], false,
                WatchEntityStateMode.None, [], [new(key, 0, [])]
            )
        ));
    }

    [TestMethod]
    public void KnownEntityPayloadShapesAreValidated()
    {
        WatchPersistentSceneState persistent = new(
            WatchPersistentSceneFlags.None,
            0,
            null,
            [],
            [],
            []
        );

        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.PersistentSession, 0), persistent.ToPayload())]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.PersistentSession, 1), persistent.ToPayload())]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.PersistentSession, 0), [0])]
            )
        ));
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.Checkpoint, 3), [1])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.SummitCheckpoint, 3), [2])]
            )
        ));
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(
                    new(WatchEntityKind.WingedStrawberry, 4),
                    [(byte)WatchWingedStrawberryState.FlyingAway]
                )]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.WingedStrawberry, 4, 1), [0])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.WingedStrawberry, 4), [3])]
            )
        ));
    }

    [TestMethod]
    public void InteractiveEntityPayloadShapesAreValidated()
    {
        WatchEntityState[] valid =
        [
            new(new(WatchEntityKind.Spring, 1), [1]),
            new(new(WatchEntityKind.Refill, 2), [(byte)WatchEntityPhase.Cooldown]),
            new(new(WatchEntityKind.FlyFeather, 3), [(byte)WatchEntityPhase.Gone]),
            new(new(WatchEntityKind.Booster, 4), new byte[16]),
            new(new(WatchEntityKind.Bumper, 5), [1, 0]),
            new(new(WatchEntityKind.Cloud, 6), new byte[10]),
            new(new(WatchEntityKind.DashSwitch, 7), new byte[9]),
            new(new(WatchEntityKind.TempleGate, 8), new byte[6]),
            new(new(WatchEntityKind.CrumblePlatform, 9), [0, 1, 0, 0]),
            new(new(WatchEntityKind.CoreMode, 0), [0]),
            new(new(WatchEntityKind.HeartGemDoor, 10), new byte[24]),
            new(new(WatchEntityKind.FakeHeart, 11), [(byte)WatchEntityPhase.Cooldown]),
            new(new(WatchEntityKind.MovingSolid, 12), new byte[24]),
            new(new(WatchEntityKind.DashBlock, 13), [1]),
            new(new(WatchEntityKind.StrawberrySeed, 14), [0]),
            new(new(WatchEntityKind.StrawberrySeed, 14, 1), new byte[10]),
            new(new(WatchEntityKind.BounceBlock, 15), new byte[60]),
            new(new(WatchEntityKind.PeriodicPlatform, 16), new byte[24]),
            new(new(WatchEntityKind.CassetteBlock, 0), new byte[24]),
            new(new(WatchEntityKind.CassetteBlock, 17, 1), CassetteBlockPayload()),
            new(new(WatchEntityKind.TouchSwitchAndSwitchGate, 18), new byte[20]),
            new(new(WatchEntityKind.ClutterSystem, 1, 3), ClutterGroupPayload()),
            new(new(WatchEntityKind.ClutterSystem, 0x50000001, 4), ClutterContactPayload()),
            new(new(WatchEntityKind.DoorMechanism, 20, 2), OshiroDoorPayload()),
        ];

        static byte[] CassetteBlockPayload()
        {
            byte[] payload = new byte[24];
            payload[0] = 1;
            return payload;
        }

        void AssertCassetteHeightValidity(int height, bool expected)
        {
            byte[] payload = CassetteBlockPayload();
            BitConverter.GetBytes(height).CopyTo(payload, 12);
            Assert.AreEqual(
                expected,
                WatchPacketValidator.IsValid(
                    new WatchSceneSnapshot(
                        Location, 0, [],
                        [new(new(WatchEntityKind.CassetteBlock, 17, 1), payload)]
                    )
                ),
                $"Expected CassetteBlock height {height} validity to be {expected}."
            );
        }

        static byte[] ClutterGroupPayload()
        {
            byte[] payload = new byte[24];
            payload[0] = 3;
            payload[1] = 1 << 2;
            payload[2] = 1;
            return payload;
        }

        static byte[] ClutterContactPayload()
        {
            byte[] payload = new byte[24];
            payload[0] = 4;
            payload[2] = 2;
            return payload;
        }

        static byte[] OshiroDoorPayload()
        {
            byte[] payload = new byte[16];
            payload[0] = 2;
            return payload;
        }

        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], valid)
        ));
        byte[] activeClutterContact = ClutterContactPayload();
        activeClutterContact[1] = 1 << 2;
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.ClutterSystem, 0x50000002, 4), activeClutterContact)]
            )
        ));
        activeClutterContact[1] = 1 << 3;
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.ClutterSystem, 0x50000002, 4), activeClutterContact)]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.Cloud, 6), new byte[9])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.Booster, 4), [(byte)WatchEntityPhase.Active])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.HeartGemDoor, 10), new byte[19])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.CrumblePlatform, 9), [0, 1, 8, 0])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.CoreMode, 1), [0])]
            )
        ));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.MovingSolid, 12), new byte[23])]
            )
        ));
        byte[] invalidClutterGroup = ClutterGroupPayload();
        invalidClutterGroup[2] = 2;
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.ClutterSystem, 1, 3), invalidClutterGroup)]
            )
        ));
        byte[] invalidClutterContact = ClutterContactPayload();
        BitConverter.GetBytes(float.NaN).CopyTo(invalidClutterContact, 8);
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.ClutterSystem, 0x50000001, 4), invalidClutterContact)]
            )
        ));
        byte[] invalidCassettePosition = CassetteBlockPayload();
        BitConverter.GetBytes(float.NaN).CopyTo(invalidCassettePosition, 4);
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.CassetteBlock, 17, 1), invalidCassettePosition)]
            )
        ));
        foreach (int height in new[] { 3, 65, 128, 208, 4096 })
            AssertCassetteHeightValidity(height, true);
        foreach (int height in new[] { -1, 4097 })
            AssertCassetteHeightValidity(height, false);
        byte[] invalidMovingState = new byte[24];
        invalidMovingState[0] = (byte)WatchMovingSolidType.BounceBlock;
        invalidMovingState[2] = 5;
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.MovingSolid, 12), invalidMovingState)]
            )
        ));

        byte[] ghostSeedState = new byte[10];
        ghostSeedState[1] = 1 << 2;
        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.StrawberrySeed, 14, 1), ghostSeedState)]
            )
        ));
        ghostSeedState[1] = 1 << 3;
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location, 0, [],
                [new(new(WatchEntityKind.StrawberrySeed, 14, 1), ghostSeedState)]
            )
        ));
    }

    [TestMethod]
    public void InteractiveEntityEventPayloadShapesAreValidated()
    {
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.Spring, 1), 1, []));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.Bumper, 2), 1, new byte[16]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.FlyFeather, 3), 1, new byte[9]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.FlyFeather, 3), 2, new byte[8]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.FlyFeather, 3), 3, []));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.FakeHeart, 4), 1, new byte[4]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.FakeHeart, 4), 2, new byte[8]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.FakeHeart, 4), 3, []));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.CrumblePlatform, 3), 1, new byte[6]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.CrumblePlatform, 3), 2, new byte[6]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.CrumblePlatform, 3), 3, new byte[4]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.DashBlock, 5), 1, new byte[18]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.BounceBlock, 6), 1, new byte[9]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.ClutterSystem, 1, 3), 1, []));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.DoorMechanism, 7, 0), 1, new byte[4]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.DoorMechanism, 8, 1), 2, [1]));
        Assert.IsTrue(IsValidEvent(new(WatchEntityKind.DoorMechanism, 9, 2), 3, []));

        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.Spring, 1), 2, []));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.Bumper, 2), 1, new byte[15]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.FlyFeather, 3), 1, new byte[8]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.FakeHeart, 4), 2, new byte[4]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.CrumblePlatform, 3), 3, new byte[6]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.DashBlock, 5), 1, new byte[17]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.BounceBlock, 6), 1, new byte[8]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.ClutterSystem, 3, 3), 1, []));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.ClutterSystem, 1, 3), 1, [0]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.DoorMechanism, 7, 0), 2, [0]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.DoorMechanism, 8, 1), 2, [2]));
        Assert.IsFalse(IsValidEvent(new(WatchEntityKind.Refill, 3), 1, []));

        static bool IsValidEvent(WatchEntityKey key, byte eventID, byte[] payload)
            => WatchPacketValidator.IsValid(new WatchSceneDelta(
                1,
                Location,
                [],
                [],
                false,
                WatchEntityStateMode.None,
                [],
                [new(key, eventID, payload)]
            ));
    }

    [TestMethod]
    public void MovingSolidPayloadTypesAndFiniteValuesAreValidated()
    {
        WatchEntityState[] valid = Enum.GetValues<WatchMovingSolidType>()
            .Where(type => type != WatchMovingSolidType.BounceBlock)
            .Select((type, index) =>
            {
                byte[] payload = new byte[24];
                payload[0] = (byte)type;
                payload[2] = type switch
                {
                    WatchMovingSolidType.SwapBlock => 1,
                    WatchMovingSolidType.MoveBlock => 2,
                    _ => 0,
                };
                return new WatchEntityState(
                    new WatchEntityKey(WatchEntityKind.MovingSolid, index),
                    payload
                );
            })
            .ToArray();

        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], valid)
        ));

        byte[] retiredBouncePayload = new byte[24];
        retiredBouncePayload[0] = (byte)WatchMovingSolidType.BounceBlock;
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(
                Location,
                0,
                [],
                [new(new(WatchEntityKind.MovingSolid, 99), retiredBouncePayload)]
            )
        ));

        byte[] unknownType = new byte[24];
        unknownType[0] = byte.MaxValue;
        byte[] unknownFlags = new byte[24];
        unknownFlags[1] = 1 << 7;
        byte[] nonZeroReserved = new byte[24];
        nonZeroReserved[3] = 1;
        byte[] notFinite = new byte[24];
        BitConverter.GetBytes(float.NaN).CopyTo(notFinite, 4);

        foreach (byte[] payload in new[] { unknownType, unknownFlags, nonZeroReserved, notFinite })
        {
            Assert.IsFalse(WatchPacketValidator.IsValid(
                new WatchSceneSnapshot(
                    Location,
                    0,
                    [],
                    [new(new(WatchEntityKind.MovingSolid, 1), payload)]
                )
            ));
        }
    }

    [TestMethod]
    public void SequenceBoundaryIsEnforced()
    {
        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, -1, [], [])));
        Assert.IsTrue(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, ["flag"], [], false)));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(1, Location, [], [], false)));
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneDelta(0, Location, [], [], false)));
    }

    [TestMethod]
    public void SceneStateMustBelongToAMap()
    {
        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(PlayerLocation.Empty, 0, [], [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneDelta(1, PlayerLocation.Empty, [], [], false)
        ));
    }

    [TestMethod]
    public void PacketPayloadBoundaryIsEnforced()
    {
        string[] largeState = Enumerable.Range(0, 31)
            .Select(index => $"{index}-" + new string('a', 998))
            .ToArray();
        byte[] largeCrumbleState = new byte[WatchPacketValidator.MaxEntityPayloadBytes];
        int imageCount = (largeCrumbleState.Length - 4) * 8;
        largeCrumbleState[2] = (byte)imageCount;
        largeCrumbleState[3] = (byte)(imageCount >> 8);
        WatchEntityState[] largeEntityState = Enumerable.Range(0, 64)
            .Select(index => new WatchEntityState(
                new WatchEntityKey(WatchEntityKind.CrumblePlatform, index),
                largeCrumbleState
            ))
            .ToArray();

        Assert.IsFalse(WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, largeState, [])));
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], largeEntityState)
        ));
    }

    [TestMethod]
    public void SnapshotBoundaryAccountsForFragmentedLogicalPayload()
    {
        List<WatchEntityState> states = Enumerable.Range(0, 63)
            .Select(index => new WatchEntityState(
                new WatchEntityKey(WatchEntityKind.CrumblePlatform, index),
                CreateCrumblePayload(WatchPacketValidator.MaxEntityPayloadBytes)
            ))
            .ToList();
        WatchSceneSnapshot partial = new(Location, 0, [], states);
        int remainingPayloadSize = Connection.MaxPayloadSize
            - GetSerializedSize(partial)
            - sizeof(ushort) * 2 - sizeof(int) - sizeof(ushort);
        Assert.IsInRange(
            4,
            WatchPacketValidator.MaxEntityPayloadBytes,
            remainingPayloadSize
        );

        states.Add(new(
            new WatchEntityKey(WatchEntityKind.CrumblePlatform, states.Count),
            CreateCrumblePayload(remainingPayloadSize)
        ));
        WatchSceneSnapshot boundary = new(Location, 0, [], states);
        Assert.AreEqual(
            Connection.MaxPayloadSize,
            GetSerializedSize(boundary)
        );
        Assert.IsTrue(WatchPacketValidator.IsValid(boundary));

        states[^1] = new(
            states[^1].Key,
            CreateCrumblePayload(remainingPayloadSize + 1)
        );
        Assert.IsFalse(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], states)
        ));

        static byte[] CreateCrumblePayload(int size)
        {
            byte[] payload = new byte[size];
            int imageCount = (size - 4) * 8;
            payload[2] = (byte)imageCount;
            payload[3] = (byte)(imageCount >> 8);
            return payload;
        }

        static int GetSerializedSize(WatchSceneSnapshot snapshot)
        {
            using MemoryStream stream = new();
            RefBinaryWriter writer = new(stream);
            writer.Write(snapshot);
            return checked((int)stream.Length);
        }
    }
}
