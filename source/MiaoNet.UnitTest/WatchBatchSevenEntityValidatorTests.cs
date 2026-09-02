using System.Buffers.Binary;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchBatchSevenEntityValidatorTests
{
    [TestMethod]
    public void BatchSevenPayloads_AcceptCanonicalShapes()
    {
        Assert.IsTrue(Valid(State(WatchEntityKind.NarrativeNPC, 56, mutate: p =>
        {
            p[0] = 0b0010_0100;
            p[4] = (byte)WatchNarrativeNPCVisual.Oshiro;
            BitConverter.TryWriteBytes(p.AsSpan(24), 1f);
            BitConverter.TryWriteBytes(p.AsSpan(44), 32f);
            BitConverter.TryWriteBytes(p.AsSpan(48), 64f);
        })));
        Assert.IsTrue(Valid(State(WatchEntityKind.AscendManager, 20)));
        Assert.IsTrue(Valid(State(WatchEntityKind.AscendManager, 20, 1, p =>
        {
            p[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), 1);
            BitConverter.TryWriteBytes(p.AsSpan(8), 0.5f);
        })));
        Assert.IsTrue(Valid(State(WatchEntityKind.IntroCar, 20)));
        Assert.IsTrue(Valid(State(WatchEntityKind.ChapterProp, 28, 1)));
        Assert.IsTrue(Valid(State(WatchEntityKind.ChapterProp, 28, 2)));
        Assert.IsTrue(Valid(State(WatchEntityKind.Lookout, 20)));
        Assert.IsTrue(Valid(State(WatchEntityKind.ConditionalBlock, 16, 1,
            p => p[1] = 0)));
        Assert.IsTrue(Valid(State(WatchEntityKind.ConditionalBlock, 16, 2,
            p => p[1] = 1)));
        Assert.IsTrue(Valid(State(WatchEntityKind.BadelineDummy, 40, mutate: p =>
        {
            p[0] = 0b0001_1111;
            BitConverter.TryWriteBytes(p.AsSpan(32), 1f);
            BitConverter.TryWriteBytes(p.AsSpan(36), 1f);
        })));
    }

    [TestMethod]
    public void BatchSevenPayloads_RejectMalformedShapes()
    {
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 36)));
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 56,
            mutate: p => p[4] = (byte)WatchNarrativeNPCVisual.BadelineBoss + 1)));
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 56,
            mutate: p => p[5] = 1)));
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 56,
            mutate: p => p[0] = 0b1000_0000)));
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 56,
            mutate: p => p[0] = 0b0000_0100)));
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 56,
            mutate: p => BitConverter.TryWriteBytes(p.AsSpan(24), 1.1f))));
        Assert.IsFalse(Valid(State(WatchEntityKind.NarrativeNPC, 56, mutate: p =>
        {
            BitConverter.TryWriteBytes(p.AsSpan(44), 64f);
            BitConverter.TryWriteBytes(p.AsSpan(48), 32f);
        })));
        Assert.IsFalse(Valid(State(WatchEntityKind.AscendManager, 20, mutate: p => p[0] = 0x10)));
        Assert.IsFalse(Valid(State(WatchEntityKind.AscendManager, 20, 1,
            p => BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), 2))));
        Assert.IsFalse(Valid(State(WatchEntityKind.AscendManager, 20, 1, p =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), 1);
            BitConverter.TryWriteBytes(p.AsSpan(8), 1.1f);
        })));
        Assert.IsFalse(Valid(State(WatchEntityKind.IntroCar, 20, mutate: p => p[1] = 1)));
        Assert.IsFalse(Valid(State(WatchEntityKind.ChapterProp, 28, 3)));
        Assert.IsFalse(Valid(State(WatchEntityKind.Lookout, 20,
            mutate: p => BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12), -1))));
        Assert.IsFalse(Valid(State(WatchEntityKind.ConditionalBlock, 16, 1,
            p => p[1] = 1)));
        Assert.IsFalse(Valid(State(WatchEntityKind.ConditionalBlock, 16, 2,
            p => BitConverter.TryWriteBytes(p.AsSpan(4), 1.1f))));
        Assert.IsFalse(Valid(State(WatchEntityKind.BadelineDummy, 40,
            mutate: p => BitConverter.TryWriteBytes(p.AsSpan(32), 1.1f))));
        Assert.IsFalse(Valid(State(WatchEntityKind.BadelineDummy, 40,
            mutate: p => BitConverter.TryWriteBytes(p.AsSpan(12), float.NaN))));
    }

    private static WatchEntityState State(
        WatchEntityKind kind,
        int length,
        ushort subID = 0,
        Action<byte[]>? mutate = null)
    {
        byte[] payload = new byte[length];
        mutate?.Invoke(payload);
        return new(new WatchEntityKey(kind, 1, subID), payload);
    }

    private static bool Valid(WatchEntityState state)
        => WatchValidatorTestHelper.IsValidState(
            new PlayerLocation("Celeste/0-Intro", AreaMode.Normal, "0"),
            state
        );
}
