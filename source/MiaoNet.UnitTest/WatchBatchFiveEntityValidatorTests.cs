using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchFiveEntityValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/10-Farewell",
        AreaMode.Normal,
        "j-06"
    );

    [TestMethod]
    public void BatchFiveEntityStatesAreStrictlyValidated()
    {
        Assert.IsTrue(IsValid(State(WatchEntityKind.LightningBreakerBox, 1, 0,
            Payload(24, p => { p[0] = 3; p[1] = 2; p[2] = 3; }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.Lightning, 2, 0,
            Payload(24, p =>
            {
                p[0] = 15;
                WriteSingle(p, 12, 1f);
                WriteSingle(p, 16, 0.6f);
                WriteSingle(p, 20, 0.5f);
            }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.BirdPath, 3, 0,
            Payload(32, p => { p[0] = 3; p[1] = 4; }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.WhiteBlock, 4, 0,
            Payload(12, p => { p[0] = 0b0001_1111; WriteSingle(p, 4, 1f); }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.ForsakenCitySatellite, 5, 0,
            Payload(16, p => { p[0] = 0b0001_1111; p[1] = 2; p[2] = 1; p[3] = 2; }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.ForsakenCitySatellite, 5, 5,
            Payload(48, p => { p[0] = 0b0001_1111; p[1] = 5; }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.ReflectionHeartStatue, 6, 0,
            Payload(12, p =>
            {
                p[0] = 7;
                p[1] = 0b0000_1111;
                p[2] = 2;
                p[4] = 1;
                p[5] = 2;
            }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.RidgeGate, 7, 0,
            Payload(24, p => p[0] = 7))));

        Assert.IsFalse(IsValid(State(WatchEntityKind.LightningBreakerBox, 1, 0,
            Payload(24, p => p[1] = 3))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lightning, 2, 0,
            Payload(24, p => p[0] = 16))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.LightningBreakerBox, 1, 0,
            Payload(24, p => p[2] = 4))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lightning, 2, 0,
            Payload(24, p => WriteSingle(p, 16, 0.61f)))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Lightning, 2, 0,
            Payload(24, p =>
            {
                p[0] = 8;
                WriteSingle(p, 20, 0.5f);
            }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.BirdPath, 3, 1, new byte[32])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.WhiteBlock, 4, 0,
            Payload(12, p => WriteSingle(p, 4, 11f)))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.ForsakenCitySatellite, 5, 0,
            Payload(16, p => { p[1] = 1; p[2] = 6; }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.ForsakenCitySatellite, 5, 6,
            new byte[48])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.ReflectionHeartStatue, 6, 0,
            Payload(12, p => { p[2] = 1; p[4] = 1; p[5] = 2; }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.RidgeGate, 7, 1, new byte[24])));
    }

    [TestMethod]
    public void BatchFiveEntityStatesRejectInvalidNumbers()
    {
        foreach ((WatchEntityKind kind, ushort subID, int size, int offset) in new[]
        {
            (WatchEntityKind.LightningBreakerBox, (ushort)0, 24, 4),
            (WatchEntityKind.Lightning, (ushort)0, 24, 20),
            (WatchEntityKind.BirdPath, (ushort)0, 32, 20),
            (WatchEntityKind.ForsakenCitySatellite, (ushort)0, 16, 8),
            (WatchEntityKind.ForsakenCitySatellite, (ushort)1, 48, 36),
            (WatchEntityKind.RidgeGate, (ushort)0, 24, 16),
        })
        {
            byte[] payload = new byte[size];
            WriteSingle(payload, offset, float.NaN);
            Assert.IsFalse(IsValid(State(kind, (int)kind, subID, payload)), kind.ToString());
        }
    }

    [TestMethod]
    public void BatchFiveVisualEventsAreStrictlyValidated()
    {
        Assert.IsTrue(IsValidEvent(WatchEntityKind.LightningBreakerBox, 0, 1, new byte[8]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.LightningBreakerBox, 0, 2, new byte[8]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Lightning, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.BirdPath, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.BirdPath, 0, 2));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.WhiteBlock, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.ForsakenCitySatellite, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.ReflectionHeartStatue, 4, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.ReflectionHeartStatue, 0, 2));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.RidgeGate, 0, 1));

        Assert.IsFalse(IsValidEvent(WatchEntityKind.LightningBreakerBox, 1, 1, new byte[8]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.Lightning, 0, 2));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.BirdPath, 0, 3));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.ForsakenCitySatellite, 1, 1));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.ReflectionHeartStatue, 5, 1));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.ReflectionHeartStatue, 1, 2));
    }

    private static WatchEntityState State(
        WatchEntityKind kind,
        int entityID,
        ushort subID,
        byte[] payload
    ) => new(new WatchEntityKey(kind, entityID, subID), payload);

    private static byte[] Payload(int size, Action<byte[]> configure)
    {
        byte[] payload = new byte[size];
        configure(payload);
        return payload;
    }

    private static void WriteSingle(byte[] payload, int offset, float value)
        => BitConverter.TryWriteBytes(payload.AsSpan(offset), value);

    private static bool IsValid(WatchEntityState state)
        => WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [], [state]));

    private static bool IsValidEvent(
        WatchEntityKind kind,
        ushort subID,
        byte eventID,
        byte[]? payload = null
    ) => WatchPacketValidator.IsValid(new WatchSceneDelta(
        1,
        Location,
        [],
        [],
        false,
        false,
        [],
        WatchEntityStateMode.None,
        [],
        [new WatchEntityEvent(
            new WatchEntityKey(kind, (int)kind, subID),
            eventID,
            payload ?? []
        )]
    ));
}
