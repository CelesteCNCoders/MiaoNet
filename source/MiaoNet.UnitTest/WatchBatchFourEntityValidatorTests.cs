using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchFourEntityValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/6-Reflection",
        AreaMode.Normal,
        "boss-00"
    );

    [TestMethod]
    public void BatchFourEntityStatesAreStrictlyValidated()
    {
        Assert.IsTrue(IsValid(State(WatchEntityKind.FinalBoss, 10, 0, Payload(36, p =>
        {
            p[0] = 0b0001_1111;
            p[1] = (byte)WatchFinalBossAnimation.LookingUp;
            p[3] = 1;
            WriteInt32(p, 4, 12);
            WriteInt32(p, 8, 15);
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.FinalBoss, 10, 0, Payload(36, p =>
            p[1] = (byte)WatchFinalBossAnimation.Unknown))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.FinalBossShot, 10, 1, new byte[56])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.FinalBossBeam, 10, 2, Payload(28, p =>
        {
            p[0] = (byte)WatchFinalBossBeamPhase.Dissipating;
            p[1] = 2;
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.FinalBossMovingBlock, 20, 0, Payload(36, p =>
        {
            p[0] = 0b0000_1111;
            WriteInt32(p, 4, 12);
            WriteInt32(p, 8, 7);
        }))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.ReflectionTentacles, 30, 3, Payload(52, p =>
        {
            p[0] = 1;
            WriteInt32(p, 4, 7);
            WriteInt32(p, 8, -1);
            WriteInt32(p, 12, 3);
        }))));

        Assert.IsFalse(IsValid(State(WatchEntityKind.FinalBoss, 10, 0, Payload(36, p =>
            p[1] = (byte)WatchFinalBossAnimation.LookingUp + 1))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FinalBoss, 10, 0, Payload(36, p =>
            p[0] = 0b0010_0000))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FinalBossShot, 10, 0, new byte[56])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FinalBossBeam, 10, 1, Payload(28, p =>
            p[0] = 3))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FinalBossMovingBlock, 20, 1, new byte[36])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.ReflectionTentacles, 30, 4, new byte[52])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.ReflectionTentacles, 30, 2, Payload(52, p =>
            WriteInt32(p, 12, 1)))));
    }

    [TestMethod]
    public void BatchFourEntityStatesRejectInvalidNumbers()
    {
        foreach ((WatchEntityKind kind, ushort subID, int size, int offset) in new[]
        {
            (WatchEntityKind.FinalBoss, (ushort)0, 36, 16),
            (WatchEntityKind.FinalBossShot, (ushort)1, 56, 4),
            (WatchEntityKind.FinalBossBeam, (ushort)1, 28, 4),
            (WatchEntityKind.FinalBossMovingBlock, (ushort)0, 36, 12),
            (WatchEntityKind.ReflectionTentacles, (ushort)0, 52, 16),
        })
        {
            byte[] payload = new byte[size];
            WriteSingle(payload, offset, float.NaN);
            Assert.IsFalse(IsValid(State(kind, (int)kind, subID, payload)), kind.ToString());
        }

        byte[] invalidHighlight = new byte[36];
        WriteSingle(invalidHighlight, 32, 1.01f);
        Assert.IsFalse(IsValid(State(
            WatchEntityKind.FinalBossMovingBlock, 20, 0, invalidHighlight)));
    }

    [TestMethod]
    public void BatchFourVisualEventsAreStrictlyValidated()
    {
        Assert.IsTrue(IsValidEvent(WatchEntityKind.FinalBoss, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.FinalBoss, 0, 2));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.FinalBossBeam, 1, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.FinalBossMovingBlock, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.ReflectionTentacles, 3, 2));

        Assert.IsFalse(IsValidEvent(WatchEntityKind.FinalBoss, 1, 1));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.FinalBossBeam, 0, 1));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.FinalBossMovingBlock, 0, 3));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.ReflectionTentacles, 4, 1));
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

    private static void WriteInt32(byte[] payload, int offset, int value)
        => BitConverter.TryWriteBytes(payload.AsSpan(offset), value);

    private static bool IsValid(WatchEntityState state)
        => WatchPacketValidator.IsValid(new WatchSceneSnapshot(Location, 0, [], [], [state]));

    private static bool IsValidEvent(WatchEntityKind kind, ushort subID, byte eventID)
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
            [new WatchEntityEvent(new WatchEntityKey(kind, (int)kind, subID), eventID, [])]
        ));
}
