using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchThreeEntityValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/5-MirrorTemple",
        AreaMode.Normal,
        "c-08b"
    );

    [TestMethod]
    public void BatchThreeEntityStatesAreStrictlyValidated()
    {
        Assert.IsTrue(IsValid(State(WatchEntityKind.SeekerSystem, 123, Payload(44, p =>
        {
            p[0] = (byte)WatchSeekerForm.Seeker;
            p[1] = (byte)WatchSeekerPhase.Returned;
            p[2] = 0b0111_1111;
            p[3] = 15;
            p[5] = 1;
            p[6] = 1;
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.SeekerBarrier, 7, new byte[16])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.PlayerSeeker, 1, Payload(72, p =>
        {
            p[0] = 0b0000_1111;
            p[1] = 15;
            WriteSingle(p, 48, 1f);
        }))));

        Assert.IsFalse(IsValid(State(WatchEntityKind.SeekerSystem, 123, Payload(44, p =>
            p[0] = 3))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.SeekerSystem, 123, Payload(44, p =>
        {
            p[0] = (byte)WatchSeekerForm.Statue;
            p[1] = (byte)WatchSeekerPhase.Attack;
        }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.SeekerSystem, 123, Payload(44, p =>
            p[3] = 16))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.SeekerBarrier, 7, Payload(16, p =>
            p[0] = 2))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PlayerSeeker, 1, Payload(72, p =>
            p[0] = 0b0001_0000))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PlayerSeeker, 1, Payload(72, p =>
            p[1] = 16))));
    }

    [TestMethod]
    public void BatchThreeEntityStatesRejectInvalidNumbersAndSubIds()
    {
        foreach ((WatchEntityKind kind, int size, int offset) in new[]
        {
            (WatchEntityKind.SeekerSystem, 44, 8),
            (WatchEntityKind.SeekerBarrier, 16, 4),
            (WatchEntityKind.PlayerSeeker, 72, 12),
        })
        {
            byte[] payload = new byte[size];
            WriteSingle(payload, offset, float.NaN);
            Assert.IsFalse(IsValid(State(kind, (int)kind, payload)), kind.ToString());
            Assert.IsFalse(IsValid(new WatchEntityState(
                new WatchEntityKey(kind, (int)kind, 1),
                new byte[size]
            )), $"{kind} sub-id");
        }

        Assert.IsFalse(IsValid(State(WatchEntityKind.PlayerSeeker, 1, Payload(72, p =>
            WriteSingle(p, 48, 2.01f)))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PlayerSeeker, 1, Payload(72, p =>
            WriteSingle(p, 52, 1.01f)))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PlayerSeeker, 1, Payload(72, p =>
            WriteSingle(p, 56, -0.01f)))));
    }

    [TestMethod]
    public void BatchThreeVisualEventsAreStrictlyValidated()
    {
        Assert.IsTrue(IsValidEvent(WatchEntityKind.SeekerSystem, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.SeekerSystem, 3, Payload(17, p => p[0] = 3)));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.SeekerBarrier, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.PlayerSeeker, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.PlayerSeeker, 2, new byte[8]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.PlayerSeeker, 3, [3, 2]));

        Assert.IsFalse(IsValidEvent(WatchEntityKind.SeekerSystem, 3, Payload(17, p => p[0] = 4)));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.SeekerSystem, 3, [3]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.SeekerBarrier, 1, [0]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.PlayerSeeker, 2, new byte[4]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.PlayerSeeker, 3, [4, 0]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.PlayerSeeker, 3, [0, 3]));
    }

    private static WatchEntityState State(WatchEntityKind kind, int entityID, byte[] payload)
        => new(new WatchEntityKey(kind, entityID), payload);

    private static byte[] Payload(int size, Action<byte[]> configure)
    {
        byte[] payload = new byte[size];
        configure(payload);
        return payload;
    }

    private static void WriteSingle(byte[] payload, int offset, float value)
        => BitConverter.TryWriteBytes(payload.AsSpan(offset), value);

    private static bool IsValid(WatchEntityState state)
        => WatchValidatorTestHelper.IsValidState(Location, state);

    private static bool IsValidEvent(WatchEntityKind kind, byte eventID, byte[] payload)
        => WatchValidatorTestHelper.IsValidEvent(
            Location,
            new(new(kind, (int)kind), eventID, payload)
        );
}
