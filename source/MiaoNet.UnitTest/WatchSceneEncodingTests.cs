using System.Buffers.Binary;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
[DoNotParallelize]
public sealed class WatchSceneEncodingTests
{
    private static readonly PlayerLocation Location = new("m", AreaMode.Normal, "r");
    private static WatchEntityState State => new(new(WatchEntityKind.Spring, 1), new byte[] { 2, 3 });

    [TestMethod]
    public void SnapshotMatchesOriginalGoldenBytes()
    {
        WatchSceneSnapshot snapshot = new(Location, 7, ["x"], [State], 9, 11);
        byte[] expected = Convert.FromHexString(
            "01006D0001007207000000090000000B00000001000100780100010001000000000002000203");
        CollectionAssert.AreEqual(expected, Raw(snapshot));
        CollectionAssert.AreEqual(expected, snapshot.EncodedPayload.ToArray());
    }

    [TestMethod]
    public void DeltaMatchesOriginalGoldenBytes()
    {
        WatchSceneDelta delta = new(8, Location, [], [], false,
            WatchEntityStateMode.Patch, [State], [], playerEpoch: 9, playerSequenceWatermark: 11);
        byte[] expected = Convert.FromHexString(
            "08000000090000000B00000001006D00010072000000000000000101000100010000000000020002030000");
        CollectionAssert.AreEqual(expected, Raw(delta));
        CollectionAssert.AreEqual(expected, delta.EncodedPayload.ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void AllSceneVariantsAndEnvelopesPreserveOriginalBytes(int variant)
    {
        WatchSceneDelta delta = new(8, Location, ["added", "中文"], ["removed"], variant == 5,
            variant == 2 ? WatchEntityStateMode.None : variant == 0 ? WatchEntityStateMode.Patch : WatchEntityStateMode.Replace,
            variant == 2 ? [] : [State], [new(State.Key, 1, new byte[] { 7 })],
            isDeathRespawn: variant == 3,
            roomTransition: variant == 4 ? new(new(Location.Map, "source"), Location,
                new Vector2(128, 64), new Vector2(1, 0)) : null,
            playerEpoch: 9, playerSequenceWatermark: 11);
        CollectionAssert.AreEqual(Raw(delta), delta.EncodedPayload.ToArray());
        WatchSceneSnapshot snapshot = new(Location, 8, ["中文", "flag"], [State], 9, 11);
        IContextualPacket[] packets =
        [
            new PacketWatchSceneDelta(delta),
            new PacketWatchSceneDeltaNotification(3, 4, delta),
            new PacketWatchSnapshotResponse(WatchSnapshotResult.Success, snapshot) { RequestID = 5 },
            new PacketWatchStartResponse(WatchStartResult.Success, 6, snapshot, 4) { RequestID = 7 },
            new PacketWatchResyncSnapshot(8, 4, snapshot),
            new PacketWatchSnapshotResponse(WatchSnapshotResult.Unavailable, null) { RequestID = 9 },
            new PacketWatchStartResponse(WatchStartResult.TargetUnavailable, 0, null) { RequestID = 10 },
        ];
        foreach (IContextualPacket packet in packets)
            AssertOriginalPacket(packet);
    }

    [TestMethod]
    public void CollectionsAreFrozenBeforeValidationAndEncoding()
    {
        string[] added = ["first"];
        List<string> removed = ["old"];
        WatchEntityState[] states = [State];
        WatchEntityEvent[] events = [new(State.Key, 1, new byte[] { 7 })];
        WatchSceneSnapshot snapshot = new(Location, 7, added, states);
        WatchSceneDelta delta = new(8, Location, added, removed, false,
            WatchEntityStateMode.Patch, states, events);
        added[0] = "changed";
        removed.Clear();
        states[0] = default;
        events[0] = default;
        Assert.AreEqual("first", snapshot.Flags.Single());
        Assert.AreEqual("first", delta.AddedFlags.Single());
        Assert.AreEqual("old", delta.RemovedFlags.Single());
        Assert.AreEqual(State.Key, delta.EntityStates.Single().Key);
        Assert.AreEqual((byte)1, delta.EntityEvents.Single().EventID);
        CollectionAssert.AreEqual(Raw(snapshot), snapshot.EncodedPayload.ToArray());
        CollectionAssert.AreEqual(Raw(delta), delta.EncodedPayload.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)delta.AddedFlags)[0] = "invalid");
    }

    [TestMethod]
    public void CachedSnapshotDoesNotCacheRequestOrSessionEnvelope()
    {
        WatchSceneSnapshot snapshot = new(Location, 7, ["flag"], [State]);
        PacketWatchStartResponse first = new(WatchStartResult.Success, 1, snapshot, 100) { RequestID = 4 };
        PacketWatchStartResponse second = new(WatchStartResult.Success, 2, snapshot, 100) { RequestID = 5 };
        AssertOriginalPacket(first);
        first.RequestID = 6;
        AssertOriginalPacket(first);
        AssertOriginalPacket(second);
        AssertOriginalPacket(new PacketWatchResyncSnapshot(3, 100, snapshot));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(10)]
    public void ConcurrentRecipientsEncodeEachSharedBodyOnlyOnce(int recipients)
    {
        long encoded = 0;
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, subscriber) =>
        {
            if (instrument.Meter.Name == "MiaoNet.WatchEncoding" && instrument.Name == "watch.scene.encoded_bodies")
                subscriber.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref encoded, value));
        listener.Start();
        WatchSceneDelta delta = CreateSizedDelta(10240);
        WatchSceneSnapshot snapshot = new(Location, 7, ["flag"], [State]);
        var bodies = new ReadOnlyMemory<byte>[recipients];
        var transfers = new int[recipients];
        Parallel.For(0, recipients, watcher =>
        {
            bodies[watcher] = delta.EncodedPayload;
            PacketWatchSceneDeltaNotification notification = new(watcher + 1, 100, delta);
            Assert.IsTrue(WatchSceneFragmenter.TryFragment(notification, out var fragments));
            var descriptor = ((PacketWatchSceneTransferStart)fragments[0]).Descriptor;
            transfers[watcher] = descriptor.TransferID;
            Assert.AreEqual(watcher + 1, descriptor.SessionID);
            AssertOriginalPacket(new PacketWatchStartResponse(WatchStartResult.Success, watcher + 1, snapshot, 100)
            { RequestID = watcher + 10 });
        });
        Assert.AreEqual(2L, encoded); // One delta and one snapshot, not one per envelope.
        Assert.HasCount(recipients, transfers.Distinct());
        foreach (ReadOnlyMemory<byte> body in bodies)
            Assert.IsTrue(body.Equals(bodies[0]));
    }

    [TestMethod]
    [DataRow(8192, false)]
    [DataRow(8193, true)]
    [DataRow(65535, true)]
    public void FragmentBoundariesAndReassembledBytesAreUnchanged(int length, bool fragmented)
    {
        WatchSceneDelta delta = CreateSizedDelta(length);
        Assert.IsTrue(WatchPacketValidator.IsValid(delta));
        byte[] raw = Raw(delta);
        Assert.HasCount(length, raw);
        Assert.AreEqual(fragmented, WatchSceneFragmenter.TryFragment(new PacketWatchSceneDelta(delta), out var fragments));
        if (!fragmented)
        {
            AssertOriginalPacket(new PacketWatchSceneDelta(delta));
            return;
        }
        WatchSceneTransferReceiver receiver = new();
        IContextualPacket? logical = null;
        foreach (IContextualPacket fragment in fragments)
            receiver.TryAccept(fragment, out logical);
        var rebuilt = (PacketWatchSceneDelta)logical!;
        CollectionAssert.AreEqual(raw, Raw(rebuilt.Delta));
        CollectionAssert.AreEqual(raw, fragments.OfType<PacketWatchSceneChunk>().SelectMany(c => c.Data).ToArray());
    }

    [TestMethod]
    public void OversizedBodyStillCannotBeFragmented()
    {
        WatchSceneDelta delta = CreateSizedDelta(65536);
        Assert.IsFalse(WatchPacketValidator.IsValid(delta));
        Assert.ThrowsExactly<InvalidDataException>(() => WatchSceneFragmenter.TryFragment(new PacketWatchSceneDelta(delta), out _));
    }

    [TestMethod]
    public void OneRecipientCannotMutateAnotherRecipientsFragmentsOrCachedBody()
    {
        WatchSceneDelta delta = CreateSizedDelta(10240);
        byte[] expected = Raw(delta);
        Assert.IsTrue(WatchSceneFragmenter.TryFragment(new PacketWatchSceneDeltaNotification(1, 100, delta), out var first));
        Assert.IsTrue(WatchSceneFragmenter.TryFragment(new PacketWatchSceneDeltaNotification(2, 100, delta), out var second));
        var firstChunk = (PacketWatchSceneChunk)first[1];
        var secondChunk = (PacketWatchSceneChunk)second[1];
        Assert.AreNotSame(firstChunk.Data, secondChunk.Data);
        firstChunk.Data[0] ^= 0xff;
        CollectionAssert.AreEqual(expected, delta.EncodedPayload.ToArray());
        CollectionAssert.AreEqual(expected, second.OfType<PacketWatchSceneChunk>().SelectMany(c => c.Data).ToArray());
    }

    [TestMethod]
    public void EncodingDoesNotKeepDeadSceneOrBodyAlive()
    {
        (WeakReference scene, WeakReference body) = CreateWeakReferences();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(scene.IsAlive);
        Assert.IsFalse(body.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference) CreateWeakReferences()
    {
        WatchSceneDelta scene = CreateSizedDelta(256);
        Assert.IsTrue(MemoryMarshal.TryGetArray(scene.EncodedPayload, out ArraySegment<byte> bytes));
        return (new(scene), new(bytes.Array!));
    }

    internal static WatchSceneDelta CreateSizedDelta(int length)
    {
        WatchSceneDelta Create(IReadOnlyCollection<WatchEntityState> states) => new(8, Location, [], [], false,
            WatchEntityStateMode.Replace, states, [], playerEpoch: 9, playerSequenceWatermark: 11);
        int remaining = length - Raw(Create([])).Length;
        List<WatchEntityState> states = [];
        while (remaining > 0)
        {
            int size = Math.Min(1024, remaining - 10);
            byte[] payload = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)((size - 4) * 8));
            states.Add(new(new(WatchEntityKind.CrumblePlatform, states.Count), payload));
            remaining -= size + 10;
        }
        return Create(states);
    }

    private static byte[] Raw<T>(T scene) where T : IRefBinarySerializable<T>
    {
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        writer.Write(scene); // Original field-by-field serialization, independent of the cache.
        return stream.ToArray();
    }

    private static void AssertOriginalPacket(IContextualPacket packet)
    {
        using MemoryStream expected = new();
        expected.Position = Connection.PacketHeaderSize;
        RefBinaryWriter writer = new(expected);
        switch (packet)
        {
            case PacketWatchSceneDelta p:
                writer.Write(p.Delta);
                break;
            case PacketWatchSceneDeltaNotification p:
                writer.Write(p.SessionID); writer.Write(p.TargetPlayerID); writer.Write(p.Delta);
                break;
            case PacketWatchSnapshotResponse p:
                writer.Write(p.RequestID); writer.Write((byte)p.Result);
                if (p.IsSuccess) writer.Write(p.Snapshot);
                break;
            case PacketWatchStartResponse p:
                writer.Write(p.RequestID); writer.Write((byte)p.Result);
                if (p.IsSuccess)
                {
                    writer.Write(p.SessionID); writer.Write(p.TargetPlayerID); writer.Write(p.Snapshot);
                }
                break;
            case PacketWatchResyncSnapshot p:
                writer.Write(p.SessionID); writer.Write(p.TargetPlayerID); writer.Write(p.Snapshot);
                break;
            default: Assert.Fail("Unexpected test packet."); break;
        }
        expected.Position = 0;
        writer.Write((ushort)(expected.Length - Connection.PacketHeaderSize));
        writer.Write(PacketRegistry.GetPacketID(packet));
        using MemoryStream actual = new();
        PacketFraming.WritePacket(actual, packet, new Context());
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
    }

    private sealed class Context : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
