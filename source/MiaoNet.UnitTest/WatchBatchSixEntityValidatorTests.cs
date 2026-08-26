using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchSixEntityValidatorTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/10-Farewell", AreaMode.Normal, "j-06"
    );

    [TestMethod]
    public void BatchSixEnvironmentStatesAreStrictlyValidated()
    {
        Assert.IsTrue(IsValid(State(WatchEntityKind.RoomEnvironment, 0, 0, new byte[72])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.RumbleTrigger, 1, 0, new byte[16])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.RumbleWall, 2, 0, [])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.Bridge, 3, 0, new byte[16])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.Bridge, 3, 1, new byte[32])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.IntroCrusher, 4, 0, new byte[28])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.ResortRoofEnding, 5, 0, [1])));
        Assert.IsTrue(IsValid(State(WatchEntityKind.ResortRoofEnding, 5, 1, new byte[28])));

        Assert.IsFalse(IsValid(State(WatchEntityKind.RoomEnvironment, 1, 0, new byte[72])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.RoomEnvironment, 0, 0,
            Payload(73, p => { p[64] = 1; p[72] = 0xff; }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.RumbleTrigger, 1, 0,
            Payload(16, p => p[0] = 8))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.RumbleWall, 2, 1, [])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.Bridge, 3, 1,
            Payload(32, p => WriteSingle(p, 16, 1.1f)))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.IntroCrusher, 4, 1, new byte[28])));
        Assert.IsFalse(IsValid(State(WatchEntityKind.ResortRoofEnding, 5, 1,
            Payload(28, p => WriteSingle(p, 24, -0.1f)))));
    }

    [TestMethod]
    public void BatchSixActorAndPresentationStatesAreStrictlyValidated()
    {
        foreach ((WatchEntityKind kind, int size) in new[]
        {
            (WatchEntityKind.BirdNPC, 44),
            (WatchEntityKind.FlutterBird, 28),
            (WatchEntityKind.MoonCreature, 48),
            (WatchEntityKind.FlingBirdIntro, 36),
            (WatchEntityKind.DreamMirror, 24),
            (WatchEntityKind.ResortMirror, 24),
            (WatchEntityKind.TempleMirrorPortal, 28),
            (WatchEntityKind.Gondola, 40),
            (WatchEntityKind.WaveDashTutorial, 36),
            (WatchEntityKind.PowerSourceNumber, 20),
        })
        {
            Assert.IsTrue(IsValid(State(kind, (int)kind, 0, new byte[size])), kind.ToString());
            Assert.IsFalse(IsValid(State(kind, (int)kind, 1, new byte[size])), kind.ToString());
            Assert.IsFalse(IsValid(State(kind, (int)kind, 0,
                Payload(size, p => WriteSingle(p, size - 4, float.NaN)))), kind.ToString());
        }

        Assert.IsFalse(IsValid(State(WatchEntityKind.BirdNPC, 1, 0,
            Payload(36, p => p[2] = 2))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.FlutterBird, 1, 0,
            Payload(28, p => p[1] = 7))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.DreamMirror, 1, 0,
            Payload(24, p => p[1] = 14))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TempleMirrorPortal, 1, 0,
            Payload(28, p => BitConverter.TryWriteBytes(p.AsSpan(4), 1025)))));
        Assert.IsTrue(IsValid(State(WatchEntityKind.TempleMirrorPortal, 1, 0,
            Payload(28, p => { p[1] = 14; p[3] = 0b0000_1111; }))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TempleMirrorPortal, 1, 0,
            Payload(28, p => p[1] = 15))));
        Assert.IsFalse(IsValid(State(WatchEntityKind.TempleMirrorPortal, 1, 0,
            Payload(28, p => p[3] = 0b0001_0000))));
        Assert.IsFalse(WatchPacketValidator.IsValid(State(
            WatchEntityKind.TempleMirrorPortal, 1, 0, Payload(28, p => p[3] = 0b0001_0000)
        )));
        Assert.IsFalse(IsValid(State(WatchEntityKind.WaveDashTutorial, 1, 0,
            Payload(36, p => BitConverter.TryWriteBytes(p.AsSpan(28), 65)))));
    }

    [TestMethod]
    public void BatchSixEventsAreStrictlyValidated()
    {
        Assert.IsTrue(IsValidEvent(WatchEntityKind.RumbleTrigger, 0, 1, new byte[4]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.RumbleWall, 0, 1));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.Bridge, 1, 1, new byte[4]));
        Assert.IsTrue(IsValidEvent(WatchEntityKind.MovingSolid, 0, 3));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.RumbleTrigger, 1, 1, new byte[4]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.RumbleTrigger, 0, 1,
            Payload(4, p => WriteSingle(p, 0, float.PositiveInfinity))));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.RumbleWall, 0, 2));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.Bridge, 0, 1, new byte[4]));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.MovingSolid, 0, 4));
        Assert.IsFalse(IsValidEvent(WatchEntityKind.BirdNPC, 0, 1));
    }

    private static WatchEntityState State(WatchEntityKind kind, int entityID, ushort subID, byte[] payload)
        => new(new WatchEntityKey(kind, entityID, subID), payload);

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

    private static bool IsValidEvent(WatchEntityKind kind, ushort subID, byte eventID, byte[]? payload = null)
        => WatchPacketValidator.IsValid(new WatchSceneDelta(
            1, Location, [], [], false, false, [], WatchEntityStateMode.None, [],
            [new WatchEntityEvent(new WatchEntityKey(kind, (int)kind, subID), eventID, payload ?? [])]
        ));
}
