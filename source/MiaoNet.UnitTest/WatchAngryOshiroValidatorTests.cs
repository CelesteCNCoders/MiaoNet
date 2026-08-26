using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchAngryOshiroValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/3-CelestialResort",
        AreaMode.Normal,
        "roof00"
    );

    [TestMethod]
    public void AngryOshiroStateAcceptsTheSingletonPayload()
    {
        byte[] payload = new byte[40];
        payload[0] = (byte)WatchAngryOshiroPhase.Hurt;
        payload[1] = 0b0011_1111;
        payload[2] = 8;
        payload[36] = 6;

        Assert.IsTrue(IsValid(new WatchEntityKey(WatchEntityKind.AngryOshiro, 0), payload));
    }

    [TestMethod]
    public void AngryOshiroStateRejectsInvalidDiscreteFields()
    {
        AssertInvalid(payload => payload[0] = 6);
        AssertInvalid(payload => payload[1] = 0b0100_0000);
        AssertInvalid(payload => payload[2] = 9);
        AssertInvalid(payload => payload[36] = 7);
        AssertInvalid(payload => payload[37] = 1);

        Assert.IsFalse(IsValid(
            new WatchEntityKey(WatchEntityKind.AngryOshiro, 1),
            new byte[40]
        ));
        Assert.IsFalse(IsValid(
            new WatchEntityKey(WatchEntityKind.AngryOshiro, 0, 1),
            new byte[40]
        ));
    }

    [TestMethod]
    public void AngryOshiroStateRejectsNonFiniteNumbers()
    {
        foreach (int offset in new[] { 4, 8, 12, 16, 20, 24, 28 })
        {
            byte[] payload = new byte[40];
            BitConverter.TryWriteBytes(payload.AsSpan(offset), float.NaN);
            Assert.IsFalse(
                IsValid(new WatchEntityKey(WatchEntityKind.AngryOshiro, 0), payload),
                $"offset {offset}"
            );
        }
    }

    private static void AssertInvalid(Action<byte[]> configure)
    {
        byte[] payload = new byte[40];
        configure(payload);
        Assert.IsFalse(IsValid(new WatchEntityKey(WatchEntityKind.AngryOshiro, 0), payload));
    }

    private static bool IsValid(WatchEntityKey key, byte[] payload)
        => WatchPacketValidator.IsValid(new WatchSceneSnapshot(
            Location,
            0,
            [],
            [],
            [new WatchEntityState(key, payload)]
        ));
}
