using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchOneHazardValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/3-CelestialResort",
        AreaMode.Normal,
        "00-a"
    );

    [TestMethod]
    public void BatchOneStatesAcceptCanonicalPayloads()
    {
        Assert.IsTrue(IsValid(State(WatchEntityKind.PeriodicPlatform, 1, 0, Payload(24, p =>
        {
            p[0] = 2;
            p[2] = 2;
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.PeriodicPlatform, 2, 0, Payload(24, p => p[0] = 3))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.PeriodicPlatform, 6, 0, Payload(24, p =>
        {
            p[0] = 1;
            p[2] = 3;
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.TriggerSpikes, 4, 17, Payload(16, p =>
        {
            p[0] = 1;
            p[1] = 3;
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.FireBall, 5, 7, Payload(24, p => p[0] = 0b0001_1111))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.Lava, 0, 1, new byte[40])));
    }

    [TestMethod]
    public void BatchOneStatesRejectInvalidDiscriminatorsReservedBitsAndFloats()
    {
        Assert.IsFalse(IsValid(State(WatchEntityKind.PeriodicPlatform, 1, 0, Payload(24, p => p[0] = 4))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PeriodicPlatform, 1, 0, Payload(24, p =>
        {
            p[0] = 2;
            p[2] = 3;
        }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PeriodicPlatform, 1, 0, Payload(24, p =>
        {
            p[0] = 1;
            p[2] = 4;
        }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.PeriodicPlatform, 1, 0, Payload(24, p =>
        {
            p[0] = 3;
            p[1] = 1 << 4;
        }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Reserved37, 3, 0, [1])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TriggerSpikes, 4, 0, Payload(16, p => p[1] = 4))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FireBall, 5, 0, Payload(24, p => p[1] = 1))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lava, 1, 0, new byte[40])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lava, 0, 2, new byte[40])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lava, 0, 0, Payload(40, p => p[0] = 1 << 4))));

        byte[] nonFinite = new byte[24];
        BitConverter.GetBytes(float.NaN).CopyTo(nonFinite, 4);
        Assert.IsFalse(IsValid(State(WatchEntityKind.FireBall, 5, 0, nonFinite)));

        byte[] lavaNonFinite = new byte[40];
        BitConverter.GetBytes(float.NaN).CopyTo(lavaNonFinite, 4);
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lava, 0, 0, lavaNonFinite)));
    }

    [TestMethod]
    public void HazardEventsAreStrictlyValidated()
    {
        WatchEntityKey reserved = new(WatchEntityKind.Reserved37, 9);
        Assert.IsFalse(IsValid(new WatchEntityEvent(reserved, 1, [0])));

        WatchEntityKey fireBall = new(WatchEntityKind.FireBall, 10, 4);
        Assert.IsTrue(IsValid(new WatchEntityEvent(fireBall, 1, [])));
        Assert.IsFalse(IsValid(new WatchEntityEvent(fireBall, 1, [0])));

        WatchEntityKey spikes = new(WatchEntityKind.TriggerSpikes, 11, 2);
        Assert.IsTrue(IsValid(new WatchEntityEvent(spikes, 1, [])));
        Assert.IsFalse(IsValid(new WatchEntityEvent(spikes, 2, [])));
    }

    [TestMethod]
    public void UndefinedKindsAreRejected()
    {
        WatchEntityKind unknown = (WatchEntityKind)ushort.MaxValue;
        Assert.IsFalse(IsValid(new WatchEntityState(new WatchEntityKey(unknown, 1), [])));
    }

    private static WatchEntityState State(
        WatchEntityKind kind,
        int id,
        ushort subID,
        byte[] payload
    ) => new(new WatchEntityKey(kind, id, subID), payload);

    private static byte[] Payload(int size, Action<byte[]> mutate)
    {
        byte[] payload = new byte[size];
        mutate(payload);
        return payload;
    }

    private static bool IsValid(WatchEntityState state)
        => WatchValidatorTestHelper.IsValidState(Location, state);

    private static bool IsValid(WatchEntityEvent entityEvent)
        => WatchValidatorTestHelper.IsValidEvent(Location, entityEvent);
}
