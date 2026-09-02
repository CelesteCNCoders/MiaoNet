using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchEntityStateTableTests
{
    [TestMethod]
    public void UnchangedCaptureProducesNoPatchAndKeepsCommittedState()
    {
        WatchEntityStateTable table = new();
        WatchEntityKey key = new(WatchEntityKind.Spring, 1);
        WatchEntityState initial = new(key, [1]);
        Commit(table, WatchEntityKind.Spring, initial);

        WatchEntityStateTable.Capture capture = table.BeginCapture();
        capture.UpdateKind(WatchEntityKind.Spring, StateMap(new WatchEntityState(key, [1])));

        Assert.IsFalse(capture.HasChanges);
        Assert.AreEqual(WatchEntityStateMode.None, capture.GetStateMode(false));
        Assert.IsEmpty(capture.GetStates(WatchEntityStateMode.None));
        WatchEntityState current = capture.EnumerateCurrentStates().Single();
        Assert.IsTrue(initial.Payload.Equals(current.Payload));
    }

    [TestMethod]
    public void ChangedCaptureIsTransactionalUntilCommitted()
    {
        WatchEntityStateTable table = new();
        WatchEntityKey key = new(WatchEntityKind.Spring, 1);
        Commit(table, WatchEntityKind.Spring, new WatchEntityState(key, [0]));

        WatchEntityStateTable.Capture changed = table.BeginCapture();
        changed.UpdateKind(WatchEntityKind.Spring, StateMap(new WatchEntityState(key, [1])));

        Assert.AreEqual(WatchEntityStateMode.Patch, changed.GetStateMode(false));
        Assert.AreEqual(1, changed.GetStates(WatchEntityStateMode.Patch).Single().Payload.Span[0]);
        Assert.AreEqual(0, table.BeginCapture().EnumerateCurrentStates().Single().Payload.Span[0]);

        changed.Commit();

        Assert.AreEqual(1, table.BeginCapture().EnumerateCurrentStates().Single().Payload.Span[0]);
    }

    [TestMethod]
    public void RemovedKeyForcesACompleteStateWithoutDroppingOtherKinds()
    {
        WatchEntityStateTable table = new();
        WatchEntityKey spring1 = new(WatchEntityKind.Spring, 1);
        WatchEntityKey spring2 = new(WatchEntityKind.Spring, 2);
        WatchEntityKey refill = new(WatchEntityKind.Refill, 3);
        WatchEntityStateTable.Capture initial = table.BeginCapture();
        initial.UpdateKind(
            WatchEntityKind.Spring,
            StateMap(
                new WatchEntityState(spring1, [0]),
                new WatchEntityState(spring2, [1])
            )
        );
        initial.UpdateKind(
            WatchEntityKind.Refill,
            StateMap(new WatchEntityState(refill, [3]))
        );
        initial.Commit();

        WatchEntityStateTable.Capture capture = table.BeginCapture();
        capture.UpdateKind(
            WatchEntityKind.Spring,
            StateMap(new WatchEntityState(spring2, [1]))
        );

        Assert.IsTrue(capture.HasRemovals);
        Assert.AreEqual(WatchEntityStateMode.Replace, capture.GetStateMode(false));
        CollectionAssert.AreEquivalent(
            new[] { spring2, refill },
            capture.GetStates(WatchEntityStateMode.Replace).Select(state => state.Key).ToArray()
        );
    }

    [TestMethod]
    public void ResetCaptureDoesNotCarryUncapturedKindsAcrossRooms()
    {
        WatchEntityStateTable table = new();
        Commit(
            table,
            WatchEntityKind.Spring,
            new WatchEntityState(new(WatchEntityKind.Spring, 1), [1])
        );

        WatchEntityStateTable.Capture capture = table.BeginCapture(resetCurrent: true);
        WatchEntityKey refill = new(WatchEntityKind.Refill, 2);
        capture.UpdateKind(
            WatchEntityKind.Refill,
            StateMap(new WatchEntityState(refill, [2]))
        );

        Assert.AreEqual(1, capture.CurrentCount);
        Assert.AreEqual(refill, capture.EnumerateCurrentStates().Single().Key);
        capture.Commit();
        Assert.AreEqual(refill, table.BeginCapture().EnumerateCurrentStates().Single().Key);
    }

    [TestMethod]
    public void TypedStateIsComparedBeforePayloadEncoding()
    {
        WatchEntityStateTable table = new();
        WatchEntityKey key = new(WatchEntityKind.Spring, 1);
        int encoded = 0;
        WatchEntityState Encode(byte value) => WatchEntityState.FromTyped(
            key,
            value,
            state =>
            {
                encoded++;
                return [state];
            }
        );

        WatchEntityStateTable.Capture initial = table.BeginCapture();
        initial.UpdateKind(WatchEntityKind.Spring, StateMap(Encode(0)));
        Assert.AreEqual(1, encoded);
        initial.Commit();

        WatchEntityStateTable.Capture unchanged = table.BeginCapture();
        unchanged.UpdateKind(WatchEntityKind.Spring, StateMap(Encode(0)));
        Assert.AreEqual(1, encoded, "Equal typed state should not allocate or encode a payload.");
        Assert.AreEqual(WatchEntityStateMode.None, unchanged.GetStateMode(false));

        WatchEntityStateTable.Capture changed = table.BeginCapture();
        changed.UpdateKind(WatchEntityKind.Spring, StateMap(Encode(1)));
        Assert.AreEqual(2, encoded);
        Assert.AreEqual(WatchEntityStateMode.Patch, changed.GetStateMode(false));
    }

    [TestMethod]
    public void InvalidKindUpdateDoesNotLeakEarlierChangesOrRemovals()
    {
        WatchEntityStateTable table = new();
        WatchEntityKey retained = new(WatchEntityKind.Spring, 1);
        WatchEntityKey removed = new(WatchEntityKind.Spring, 2);
        WatchEntityStateTable.Capture initial = table.BeginCapture();
        initial.UpdateKind(
            WatchEntityKind.Spring,
            StateMap(
                new WatchEntityState(retained, [0]),
                new WatchEntityState(removed, [1])
            )
        );
        initial.Commit();

        WatchEntityStateTable.Capture capture = table.BeginCapture();
        Dictionary<WatchEntityKey, WatchEntityState> invalid = StateMap(
            new WatchEntityState(retained, [1]),
            new WatchEntityState(new(WatchEntityKind.Refill, 3), [1])
        );

        Assert.ThrowsExactly<InvalidOperationException>(
            () => capture.UpdateKind(WatchEntityKind.Spring, invalid)
        );
        Assert.IsFalse(capture.HasChanges);
        Assert.AreEqual(WatchEntityStateMode.None, capture.GetStateMode(false));
        CollectionAssert.AreEquivalent(
            new[] { retained, removed },
            capture.EnumerateCurrentStates().Select(state => state.Key).ToArray()
        );
    }

    [TestMethod]
    public void SizedDeferredPayloadIsEncodedOnceOnFirstAccess()
    {
        int encoded = 0;
        WatchEntityState state = WatchEntityState.FromTyped(
            new(WatchEntityKind.Spring, 1),
            (First: (byte)7, Second: (byte)9),
            2,
            (payload, value) =>
            {
                encoded++;
                payload[0] = value.First;
                payload[1] = value.Second;
            }
        );

        Assert.AreEqual(0, encoded);
        CollectionAssert.AreEqual(new byte[] { 7, 9 }, state.Payload.ToArray());
        CollectionAssert.AreEqual(new byte[] { 7, 9 }, state.Payload.ToArray());
        Assert.AreEqual(1, encoded);
    }

    [TestMethod]
    public void StructuralTypedComparerSkipsEncodingForEquivalentArrays()
    {
        WatchEntityStateTable table = new();
        WatchEntityKey key = new(
            WatchEntityKind.TouchSwitchAndSwitchGate,
            0,
            1
        );
        int encoded = 0;
        WatchEntityState Encode(int[] values) => WatchEntityState.FromTyped(
            key,
            values,
            state =>
            {
                encoded++;
                byte[] payload = new byte[state.Length * sizeof(int)];
                for (int index = 0; index < state.Length; index++)
                    BitConverter.TryWriteBytes(payload.AsSpan(index * sizeof(int)), state[index]);
                return payload;
            },
            WatchArrayEqualityComparer<int>.Instance
        );

        WatchEntityStateTable.Capture initial = table.BeginCapture();
        initial.UpdateKind(WatchEntityKind.TouchSwitchAndSwitchGate, StateMap(Encode([1, 2])));
        initial.Commit();
        Assert.AreEqual(1, encoded);

        WatchEntityStateTable.Capture unchanged = table.BeginCapture();
        unchanged.UpdateKind(
            WatchEntityKind.TouchSwitchAndSwitchGate,
            StateMap(Encode([1, 2]))
        );
        Assert.IsFalse(unchanged.HasChanges);
        Assert.AreEqual(1, encoded);
    }

    private static void Commit(
        WatchEntityStateTable table,
        WatchEntityKind kind,
        params WatchEntityState[] states
    )
    {
        WatchEntityStateTable.Capture capture = table.BeginCapture();
        capture.UpdateKind(kind, StateMap(states));
        capture.Commit();
    }

    private static Dictionary<WatchEntityKey, WatchEntityState> StateMap(
        params WatchEntityState[] states
    ) => states.ToDictionary(state => state.Key);
}
