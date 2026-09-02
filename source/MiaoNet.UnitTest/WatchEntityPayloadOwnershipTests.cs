using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchEntityPayloadOwnershipTests
{
    [TestMethod]
    public void PublicConstructorsStillCopyCallerBuffers()
    {
        byte[] input = [1, 2, 3];
        WatchEntityKey key = new(WatchEntityKind.Spring, 7);
        WatchEntityState state = new(key, input);
        WatchEntityEvent entityEvent = new(key, 1, input);
        input.AsSpan().Clear();
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, state.Payload.ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, entityEvent.Payload.ToArray());
    }

    [TestMethod]
    public void DeserializedPayloadsDoNotBorrowReusableReceiveBuffer()
    {
        WatchEntityKey key = new(WatchEntityKind.Spring, 7);
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        writer.Write(new WatchEntityState(key, new byte[] { 1, 2, 3 }));
        writer.Write(new WatchEntityEvent(key, 4, new byte[] { 5, 6 }));
        byte[] input = stream.ToArray();
        RefBinaryReader reader = new(input);
        WatchEntityState state = reader.Read<WatchEntityState>();
        WatchEntityEvent entityEvent = reader.Read<WatchEntityEvent>();
        input.AsSpan().Clear();
        Assert.AreEqual(0, reader.BytesLeft);
        Assert.AreEqual(key, state.Key);
        Assert.AreEqual(key, entityEvent.Key);
        Assert.AreEqual((byte)4, entityEvent.EventID);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, state.Payload.ToArray());
        CollectionAssert.AreEqual(new byte[] { 5, 6 }, entityEvent.Payload.ToArray());
    }

    [TestMethod]
    public void DeserializationAllocatesOnePayloadArrayNotTwo()
    {
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        writer.Write(new WatchEntityState(new(WatchEntityKind.CrumblePlatform, 7), new byte[1024]));
        byte[] input = stream.ToArray();
        for (int i = 0; i < 10; i++) Decode(input);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Decode(input);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsTrue(allocated >= 1024 * 100 && allocated < 1500 * 100, $"Unexpected allocation: {allocated}.");
    }

    private static void Decode(byte[] input)
    {
        RefBinaryReader reader = new(input);
        _ = reader.Read<WatchEntityState>();
    }
}
