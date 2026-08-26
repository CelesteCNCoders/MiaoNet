using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchNextBatchEntityValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/5-MirrorTemple",
        AreaMode.Normal,
        "a-00"
    );

    [TestMethod]
    public void NextBatchEntityStatesAreAccepted()
    {
        WatchEntityState[] states =
        [
            State(WatchEntityKind.Key, new byte[12]),
            State(WatchEntityKind.LockBlock, new byte[4]),
            State(WatchEntityKind.TheoCrystal, new byte[24]),
            State(WatchEntityKind.Glider, new byte[24]),
            State(WatchEntityKind.TheoCrystalPedestal, [0]),
            State(WatchEntityKind.BadelineBoost, new byte[16]),
            State(WatchEntityKind.FlingBird, new byte[20]),
            State(WatchEntityKind.WallBooster, [0, 1]),
            State(WatchEntityKind.Torch, [1]),
            State(WatchEntityKind.TempleCrackedBlock, [1]),
            State(WatchEntityKind.TempleBigEyeball, [1, 1]),
        ];

        Assert.IsTrue(WatchPacketValidator.IsValid(
            new WatchSceneSnapshot(Location, 0, [], [], states)
        ));
    }

    [TestMethod]
    public void NextBatchEntityStatesRejectMalformedPayloads()
    {
        byte[] invalidKeyPhase = new byte[12];
        invalidKeyPhase[0] = byte.MaxValue;
        byte[] invalidHoldableNumber = new byte[24];
        BitConverter.TryWriteBytes(invalidHoldableNumber.AsSpan(4), float.NaN);
        byte[] validGoneHoldable = new byte[24];
        validGoneHoldable[0] = (byte)WatchHoldablePhase.Gone;
        byte[] invalidHoldablePhase = new byte[24];
        invalidHoldablePhase[0] = (byte)WatchHoldablePhase.Gone + 1;
        byte[] invalidBirdState = new byte[20];
        invalidBirdState[0] = 5;
        byte[] validBadelineBoost = new byte[16];
        validBadelineBoost[1] = 0b0001_1111;
        BitConverter.TryWriteBytes(validBadelineBoost.AsSpan(12), 0.5f);
        byte[] invalidBadelineBoostFlags = (byte[])validBadelineBoost.Clone();
        invalidBadelineBoostFlags[1] |= 0b0010_0000;
        byte[] invalidBadelineBoostProgress = (byte[])validBadelineBoost.Clone();
        BitConverter.TryWriteBytes(invalidBadelineBoostProgress.AsSpan(12), 1.01f);

        Assert.IsFalse(IsValid(State(WatchEntityKind.Key, invalidKeyPhase)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.LockBlock, new byte[3])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TheoCrystal, invalidHoldableNumber)));
        Assert.IsTrue(IsValid(State(WatchEntityKind.TheoCrystal, validGoneHoldable)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TheoCrystal, invalidHoldablePhase)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Glider, new byte[23])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TheoCrystalPedestal, [2])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.BadelineBoost, new byte[15])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.BadelineBoost, validBadelineBoost)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.BadelineBoost, invalidBadelineBoostFlags)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.BadelineBoost, invalidBadelineBoostProgress)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FlingBird, invalidBirdState)));
        Assert.IsFalse(IsValid(State(WatchEntityKind.WallBooster, [2, 0])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Torch, [2])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TempleCrackedBlock, [2])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TempleBigEyeball, [0, 2])));
    }

    [TestMethod]
    public void NextBatchEntityEventsAreValidated()
    {
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Key, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Key, 2, new byte[12]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Key, 3, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.LockBlock, 1, new byte[4]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.TheoCrystal, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.TheoCrystal, 2, new byte[16]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Glider, 3, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.BadelineBoost, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.FlingBird, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Torch, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.TempleCrackedBlock, 1, new byte[8]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.TempleBigEyeball, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.TempleBigEyeball, 2, []));

        Assert.IsFalse(IsValidEvent(WatchEntityKind.Key, 2, new byte[11]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.LockBlock, 2, new byte[4]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.TheoCrystal, 2, new byte[15]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.WallBooster, 1, []));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.TempleBigEyeball, 3, []));
    }

    private static WatchEntityState State(WatchEntityKind kind, byte[] payload)
        => new(new WatchEntityKey(kind, (int)kind), payload);

    private static bool IsValid(WatchEntityState state)
        => WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [], [state]));

    private static bool IsValidEvent(WatchEntityKind kind, byte eventID, byte[] payload)
        => WatchPacketValidator.IsValid(new WatchSceneDelta(
            1,
            Location,
            [],
            [],
            false,
            false,
            [],
            WatchEntityStateMode.None,
            [],
            [new WatchEntityEvent(new WatchEntityKey(kind, (int)kind), eventID, payload)]
        ));
}
