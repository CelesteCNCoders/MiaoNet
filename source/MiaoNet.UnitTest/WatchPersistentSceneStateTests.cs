using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchPersistentSceneStateTests
{
    [TestMethod]
    public void PayloadRoundTripPreservesPersistentRoomState()
    {
        WatchPersistentSceneState expected = new(
            WatchPersistentSceneFlags.Cassette
                | WatchPersistentSceneFlags.HitCheckpoint
                | WatchPersistentSceneFlags.HasRespawnPoint
                | WatchPersistentSceneFlags.CassetteGhost
                | WatchPersistentSceneFlags.FakeHeart,
            0b0010_0101,
            new Vector2(24f, -8f),
            [9, 2],
            [7, 3],
            [11, 5]
        );

        bool success = WatchPersistentSceneState.TryFromPayload(
            expected.ToPayload(),
            out WatchPersistentSceneState? actual
        );

        Assert.IsTrue(success);
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Flags, actual.Flags);
        Assert.AreEqual(expected.SummitGems, actual.SummitGems);
        Assert.AreEqual(expected.RespawnPoint, actual.RespawnPoint);
        CollectionAssert.AreEqual(expected.DoNotLoadIDs.ToArray(), actual.DoNotLoadIDs.ToArray());
        CollectionAssert.AreEqual(expected.StrawberryIDs.ToArray(), actual.StrawberryIDs.ToArray());
        CollectionAssert.AreEqual(
            expected.GhostStrawberryIDs.ToArray(),
            actual.GhostStrawberryIDs.ToArray()
        );
    }

    [TestMethod]
    public void InvalidFlagsSummitBitsAndIDsAreRejected()
    {
        WatchPersistentSceneState invalidFlags = new(
            (WatchPersistentSceneFlags)(1 << 7),
            0,
            null,
            [],
            [],
            []
        );
        WatchPersistentSceneState invalidSummitGems = new(
            WatchPersistentSceneFlags.None,
            0b1000_0000,
            null,
            [],
            [],
            []
        );
        WatchPersistentSceneState duplicateIDs = new(
            WatchPersistentSceneFlags.None,
            0,
            null,
            [4, 4],
            [],
            []
        );
        WatchPersistentSceneState negativeID = new(
            WatchPersistentSceneFlags.None,
            0,
            null,
            [],
            [],
            [-1]
        );

        Assert.IsFalse(WatchPersistentSceneState.TryFromPayload(invalidFlags.ToPayload(), out _));
        Assert.IsFalse(WatchPersistentSceneState.TryFromPayload(invalidSummitGems.ToPayload(), out _));
        Assert.IsFalse(WatchPersistentSceneState.TryFromPayload(duplicateIDs.ToPayload(), out _));
        Assert.IsFalse(WatchPersistentSceneState.TryFromPayload(negativeID.ToPayload(), out _));
    }

    [TestMethod]
    public void TruncatedOrTrailingPayloadIsRejected()
    {
        WatchPersistentSceneState state = new(
            WatchPersistentSceneFlags.None,
            0,
            null,
            [1],
            [2],
            [3]
        );
        byte[] payload = state.ToPayload();

        Assert.IsFalse(WatchPersistentSceneState.TryFromPayload(payload.AsSpan(0, payload.Length - 1), out _));
        Assert.IsFalse(WatchPersistentSceneState.TryFromPayload([.. payload, 0], out _));
    }

    [TestMethod]
    public void RespawnPointPresenceMustMatchFlagsAndBeFinite()
    {
        WatchPersistentSceneState missingPoint = new(
            WatchPersistentSceneFlags.HasRespawnPoint,
            0,
            null,
            [],
            [],
            []
        );
        WatchPersistentSceneState unexpectedPoint = new(
            WatchPersistentSceneFlags.None,
            0,
            new Vector2(1f, 2f),
            [],
            [],
            []
        );
        WatchPersistentSceneState nonFinitePoint = new(
            WatchPersistentSceneFlags.HasRespawnPoint,
            0,
            new Vector2(float.NaN, 2f),
            [],
            [],
            []
        );

        Assert.IsFalse(missingPoint.IsValid());
        Assert.IsFalse(unexpectedPoint.IsValid());
        Assert.IsFalse(nonFinitePoint.IsValid());
    }
}
