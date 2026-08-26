using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchTwoEntityValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/3-CelestialResort",
        AreaMode.Normal,
        "roof00"
    );

    [TestMethod]
    public void BatchTwoEntityStatesAreStrictlyValidated()
    {
        Assert.IsTrue(IsValid(State(WatchEntityKind.Snowball, 0, Payload(24, p =>
        {
            p[0] = 1;
            p[2] = 1;
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.Puffer, 0, Payload(48, p => p[2] = 6))));

        Assert.IsFalse(IsValid(State(WatchEntityKind.Snowball, 0, Payload(24, p => p[0] = 2))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Snowball, 0, Payload(24, p => p[2] = 2))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Snowball, 0, Payload(24, p => p[0] = 1))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Puffer, 0, Payload(48, p => p[0] = 3))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Puffer, 0, Payload(48, p => p[2] = 7))));
    }

    [TestMethod]
    public void BatchTwoEntityStatesRejectNonFiniteNumbers()
    {
        foreach (WatchEntityKind kind in new[]
        {
            WatchEntityKind.Snowball,
            WatchEntityKind.Puffer,
        })
        {
            int size = kind == WatchEntityKind.Puffer ? 48 : 24;
            byte[] payload = new byte[size];
            BitConverter.TryWriteBytes(payload.AsSpan(4), float.NaN);
            Assert.IsFalse(IsValid(State(kind, 0, payload)), kind.ToString());
        }
    }

    [TestMethod]
    public void BatchTwoVisualEventsAreStrictlyValidated()
    {
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Snowball, 1, []));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Snowball, 2, [0]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Snowball, 2, [1]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Puffer, 1, []));

        Assert.IsFalse(IsValidEvent(WatchEntityKind.Snowball, 2, [2]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.Snowball, 3, []));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.Puffer, 1, [0]));
    }

    private static WatchEntityState State(WatchEntityKind kind, ushort subID, byte[] payload)
        => new(new WatchEntityKey(kind, (int)kind, subID), payload);

    private static byte[] Payload(int size, Action<byte[]> configure)
    {
        byte[] payload = new byte[size];
        configure(payload);
        return payload;
    }

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
