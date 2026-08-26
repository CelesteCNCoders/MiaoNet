using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBadelineOldsiteValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/2-OldSite",
        AreaMode.Normal,
        "4"
    );

    [TestMethod]
    public void LifecycleStateAcceptsVanillaChaseDelay()
    {
        byte[] payload = ValidPayload();
        Assert.IsTrue(IsValid(payload));
    }

    [TestMethod]
    public void LifecycleStateRejectsUnknownFlagsAndAnimation()
    {
        byte[] payload = ValidPayload();
        payload[0] = 0x80;
        Assert.IsFalse(IsValid(payload));

        payload = ValidPayload();
        payload[1] = 39;
        Assert.IsFalse(IsValid(payload));
    }

    [TestMethod]
    public void LifecycleStateRejectsInvalidDelayAndNonFinitePosition()
    {
        byte[] payload = ValidPayload();
        BitConverter.TryWriteBytes(payload.AsSpan(16), 3f);
        Assert.IsFalse(IsValid(payload));

        payload = ValidPayload();
        BitConverter.TryWriteBytes(payload.AsSpan(4), float.NaN);
        Assert.IsFalse(IsValid(payload));
    }

    [TestMethod]
    public void InitialHistoryChunksAreStrictlyValidated()
    {
        byte[] payload = new byte[13];
        payload[0] = 1;
        payload[1] = 0;
        payload[2] = 1;
        payload[3] = 1;
        BitConverter.TryWriteBytes(payload.AsSpan(4), 24f);
        BitConverter.TryWriteBytes(payload.AsSpan(8), 48f);
        payload[12] = 0b0100_0000;
        Assert.IsTrue(IsValid(payload, entityID: 0, subID: 1));

        payload[12] = 39;
        Assert.IsFalse(IsValid(payload, entityID: 0, subID: 1));
        payload[12] = 63;
        Assert.IsTrue(IsValid(payload, entityID: 0, subID: 1));
        payload[12] = 0;
        payload[3] = 2;
        Assert.IsFalse(IsValid(payload, entityID: 0, subID: 1));
        payload[3] = 1;
        Assert.IsFalse(IsValid(payload, entityID: 1, subID: 1));
    }

    private static byte[] ValidPayload()
    {
        byte[] payload = new byte[28];
        payload[0] = 0b0010_1011;
        payload[1] = 0;
        payload[3] = 2;
        BitConverter.TryWriteBytes(payload.AsSpan(4), 128f);
        BitConverter.TryWriteBytes(payload.AsSpan(8), 64f);
        BitConverter.TryWriteBytes(payload.AsSpan(12), 1.55f);
        BitConverter.TryWriteBytes(payload.AsSpan(16), 0.8f);
        BitConverter.TryWriteBytes(payload.AsSpan(20), 2f);
        BitConverter.TryWriteBytes(payload.AsSpan(24), -1);
        return payload;
    }

    private static bool IsValid(byte[] payload, int entityID = 41, ushort subID = 0)
        => WatchValidatorTestHelper.IsValidState(
            Location,
            new(new(WatchEntityKind.BadelineOldsite, entityID, subID), payload)
        );
}
