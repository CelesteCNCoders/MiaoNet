using System.Collections;
using System.Reflection;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchSceneTransferLifetimeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("complete")]
    [DataRow("clear")]
    [DataRow("cancel")]
    [DataRow("cancel-all")]
    [DataRow("target")]
    [DataRow("discardable")]
    [DataRow("duplicate")]
    [DataRow("length")]
    [DataRow("index")]
    [DataRow("descriptor")]
    public void TerminatedTransferImmediatelyReleasesBuffers(string action)
    {
        PacketWatchSceneDeltaNotification packet = new(7, 100, WatchSceneEncodingTests.CreateSizedDelta(10240));
        Assert.IsTrue(WatchSceneFragmenter.TryFragment(packet, out var fragments));
        var start = (PacketWatchSceneTransferStart)fragments[0];
        if (action == "descriptor")
        {
            var d = start.Descriptor;
            start = new(new(d.TransferID, d.Kind, d.TotalLength, d.FragmentCount,
                d.SceneSequence + 1, d.PlayerEpoch, d.PlayerSequenceWatermark,
                d.RequestID, d.SessionID, d.TargetPlayerID));
        }
        WatchSceneTransferReceiver receiver = new();
        receiver.TryAccept(start, out _);
        receiver.TryAccept(fragments[1], out _);
        // Hold exactly the fragment table that the timeout task can keep alive.
        Array retained = GetPendingFragmentTable(receiver, start.Descriptor.TransferID);
        Assert.IsNotNull(retained.GetValue(0));
        var tail = (PacketWatchSceneChunk)fragments[2];
        switch (action)
        {
            case "complete":
                receiver.TryAccept(tail, out IContextualPacket? logical);
                Assert.IsInstanceOfType<PacketWatchSceneDeltaNotification>(logical);
                break;
            case "clear": receiver.Clear(); break;
            case "cancel": receiver.TryAccept(new PacketWatchSceneCancel(start.Descriptor.TransferID), out _); break;
            case "cancel-all": receiver.TryAccept(new PacketWatchSceneCancel(0), out _); break;
            case "target": receiver.ClearForTarget(100); break;
            case "discardable": receiver.ClearDiscardable(); break;
            case "duplicate":
                Assert.ThrowsExactly<InvalidDataException>(() => receiver.TryAccept(fragments[1], out _));
                break;
            case "length":
                Assert.ThrowsExactly<InvalidDataException>(() => receiver.TryAccept(
                    new PacketWatchSceneChunk(tail.TransferID, tail.FragmentIndex, tail.Data[..^1]), out _));
                break;
            case "index":
                Assert.ThrowsExactly<InvalidDataException>(() => receiver.TryAccept(
                    new PacketWatchSceneChunk(tail.TransferID, start.Descriptor.FragmentCount, tail.Data), out _));
                break;
            case "descriptor":
                Assert.ThrowsExactly<InvalidDataException>(() => receiver.TryAccept(tail, out _));
                break;
        }
        AssertReleased(retained);
        receiver.TryAccept(tail, out IContextualPacket? ignored);
        Assert.IsNull(ignored);
    }

    [TestMethod]
    public async Task UnfinishedTransferStillExpiresAndReleasesBuffers()
    {
        PacketWatchSceneDelta packet = new(WatchSceneEncodingTests.CreateSizedDelta(10240));
        Assert.IsTrue(WatchSceneFragmenter.TryFragment(packet, out var fragments));
        WatchSceneTransferReceiver receiver = new();
        var start = (PacketWatchSceneTransferStart)fragments[0];
        receiver.TryAccept(start, out _);
        receiver.TryAccept(fragments[1], out _);
        Array retained = GetPendingFragmentTable(receiver, start.Descriptor.TransferID);
        await Task.Delay(TimeSpan.FromSeconds(3.2), TestContext.CancellationToken);
        receiver.TryAccept(fragments[2], out IContextualPacket? ignored);
        Assert.IsNull(ignored);
        AssertReleased(retained);
    }

    [TestMethod]
    public void ClearingAnotherTargetDoesNotReleaseActiveOrRequiredTransfers()
    {
        var delta = WatchSceneEncodingTests.CreateSizedDelta(10240);
        var snapshot = new WatchSceneSnapshot(delta.Location, 7, [], delta.EntityStates, 9, 11);
        IContextualPacket[] packets =
        [
            new PacketWatchSceneDeltaNotification(7, 100, delta),
            new PacketWatchStartResponse(WatchStartResult.Success, 8, snapshot, 100),
        ];
        foreach (IContextualPacket packet in packets)
        {
            Assert.IsTrue(WatchSceneFragmenter.TryFragment(packet, out var fragments));
            var start = (PacketWatchSceneTransferStart)fragments[0];
            WatchSceneTransferReceiver receiver = new();
            receiver.TryAccept(start, out _);
            receiver.TryAccept(fragments[1], out _);
            Array retained = GetPendingFragmentTable(receiver, start.Descriptor.TransferID);
            receiver.ClearForTarget(101);
            if (packet is PacketWatchStartResponse)
            {
                receiver.ClearForTarget(100);
                receiver.ClearDiscardable();
            }
            Assert.IsNotNull(retained.GetValue(0));
            receiver.Clear();
            AssertReleased(retained);
        }
    }

    private static Array GetPendingFragmentTable(WatchSceneTransferReceiver receiver, int id)
    {
        var pending = (IDictionary)typeof(WatchSceneTransferReceiver)
            .GetField("pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(receiver)!;
        object transfer = pending[id]!;
        return (Array)transfer.GetType().GetProperty("Fragments", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(transfer)!;
    }

    private static void AssertReleased(Array fragments)
        => Assert.IsTrue(fragments.Cast<object?>().All(fragment => fragment is null));
}
