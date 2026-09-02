using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchSessionRegistryTests
{
    private static readonly PlayerMapLocation Map = new("Celeste/1-ForsakenCity", AreaMode.Normal);

    [TestMethod]
    public void AddIndexesSessionByWatcherAndTarget()
    {
        WatchSessionRegistry registry = new();

        WatchSession session = registry.Add(1, 2, Map, 3);

        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.TryGet(session.ID, out WatchSession? byID));
        Assert.AreSame(session, byID);
        Assert.IsTrue(registry.TryGetByWatcher(1, out WatchSession? byWatcher));
        Assert.AreSame(session, byWatcher);
        CollectionAssert.AreEqual(new[] { session }, registry.GetByTarget(2).ToArray());
    }

    [TestMethod]
    public void TargetCanHaveMultipleWatchers()
    {
        WatchSessionRegistry registry = new();
        WatchSession first = registry.Add(1, 3, Map, 4);
        WatchSession second = registry.Add(2, 3, Map, 5);

        IReadOnlyCollection<WatchSession> sessions = registry.GetByTarget(3);

        Assert.HasCount(2, sessions);
        CollectionAssert.AreEquivalent(new[] { first, second }, sessions.ToArray());
    }

    [TestMethod]
    public void RemoveAllForPlayerRemovesProducedAndWatchedSessions()
    {
        WatchSessionRegistry registry = new();
        WatchSession watched = registry.Add(1, 2, Map, 3);
        WatchSession produced = registry.Add(4, 1, Map, 5);
        WatchSession unrelated = registry.Add(6, 7, Map, 8);

        IReadOnlyCollection<WatchSession> removed = registry.RemoveAllForPlayer(1);

        CollectionAssert.AreEquivalent(new[] { watched, produced }, removed.ToArray());
        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.TryGet(unrelated.ID, out _));
        Assert.IsFalse(registry.HasWatcher(1));
        Assert.IsFalse(registry.HasTarget(1));
    }

    [TestMethod]
    public void SequenceGapPausesUntilSnapshotEstablishesANewBaseline()
    {
        WatchSession session = new(1, 2, 3, Map, 4);

        Assert.AreEqual(WatchSequenceResult.Inactive, session.AcceptSequence(1));
        session.Activate(5);
        Assert.AreEqual(WatchSequenceResult.Gap, session.AcceptSequence(7));
        Assert.IsTrue(session.IsResyncPending);
        Assert.AreEqual(5, session.LastSequence);
        Assert.AreEqual(WatchSequenceResult.ResyncPending, session.AcceptSequence(6));

        session.CompleteResync(8);

        Assert.IsFalse(session.IsResyncPending);
        Assert.AreEqual(WatchSequenceResult.Duplicate, session.AcceptSequence(8));
        Assert.AreEqual(WatchSequenceResult.Next, session.AcceptSequence(9));
        Assert.AreEqual(9, session.LastSequence);
    }

    [TestMethod]
    public void WatcherCanRequestResyncOnlyWhenItIsBehindTheServer()
    {
        TimeSpan cooldown = TimeSpan.FromSeconds(2);
        WatchSession session = new(1, 2, 3, Map, 4);
        session.Activate(5);

        Assert.IsFalse(session.TryBeginResync(5, TimeSpan.Zero, cooldown));
        Assert.IsFalse(session.TryBeginResync(6, TimeSpan.Zero, cooldown));
        Assert.IsTrue(session.TryBeginResync(4, TimeSpan.Zero, cooldown));
        Assert.IsFalse(session.TryBeginResync(4, TimeSpan.Zero, cooldown));

        session.CompleteResync(7);

        Assert.AreEqual(7, session.LastSequence);
        Assert.IsFalse(session.IsResyncPending);
        Assert.IsFalse(session.TryBeginResync(4, cooldown, cooldown));
        Assert.AreEqual(WatchSequenceResult.Next, session.AcceptSequence(8));
        Assert.IsFalse(session.TryBeginResync(7, cooldown - TimeSpan.FromTicks(1), cooldown));
        Assert.IsTrue(session.TryBeginResync(7, cooldown, cooldown));
    }

    [TestMethod]
    public void RestartSuspensionAcceptsOnlyItsEmptyBarrierAndSameMapReturn()
    {
        PlayerLocation empty = PlayerLocation.Empty;
        PlayerLocation sameMap = new(Map, "2");
        PlayerLocation otherMap = new(
            new PlayerMapLocation("Celeste/2-OldSite", AreaMode.Normal),
            "1"
        );
        WatchSession session = new(1, 2, 3, Map, 4);
        session.Activate(5, 7);
        session.SuspendForRestart(
            WatchTargetRestartKind.GoldenBerryRestart,
            8,
            TimeSpan.FromSeconds(30)
        );

        Assert.IsTrue(session.IsRestartSuspended);
        Assert.AreEqual(
            WatchSequenceResult.RestartSuspended,
            session.AcceptSequence(6, 7)
        );
        Assert.IsTrue(session.CanContinueRestartAt(empty, 8, out bool emptyResync));
        Assert.IsFalse(emptyResync);
        Assert.IsFalse(session.CanContinueRestartAt(otherMap, 9, out _));
        Assert.IsFalse(session.CanContinueRestartAt(sameMap, 10, out _));
        Assert.IsTrue(session.CanContinueRestartAt(sameMap, 9, out bool returnResync));
        Assert.IsTrue(returnResync);

        session.BeginRestartResync();
        session.CompleteResync(7, 9);

        Assert.IsFalse(session.IsRestartSuspended);
        Assert.IsFalse(session.IsResyncPending);
        Assert.AreEqual((uint)9, session.PlayerEpoch);
        Assert.AreEqual(WatchSequenceResult.Next, session.AcceptSequence(8, 9));
    }

    [TestMethod]
    public void RestartSuspensionExpiresOnlyForItsOwnGeneration()
    {
        WatchSession session = new(1, 2, 3, Map, 4);
        session.Activate(0);
        session.SuspendForRestart(
            WatchTargetRestartKind.RestartChapter,
            3,
            TimeSpan.FromSeconds(10)
        );

        Assert.IsFalse(session.IsRestartExpired(TimeSpan.FromSeconds(9), 3));
        Assert.IsFalse(session.IsRestartExpired(TimeSpan.FromSeconds(10), 4));
        Assert.IsTrue(session.IsRestartExpired(TimeSpan.FromSeconds(10), 3));
    }
}
