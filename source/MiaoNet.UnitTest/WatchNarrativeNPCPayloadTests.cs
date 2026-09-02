using System.Buffers.Binary;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchNarrativeNPCPayloadTests
{
    private static readonly PlayerLocation Location = new("Celeste/3-CelestialResort", AreaMode.Normal, "a-00");

    [TestMethod]
    public void CompactStatePreservesOriginalLayoutAndUses46WireBytes()
    {
        WatchEntityState state = CreateState();
        Assert.IsTrue(WatchPacketValidator.IsValid(state));
        byte[] encoded = Encode(state);
        // 8-byte entity key + 2-byte payload length + the original 36-byte body.
        Assert.HasCount(46, encoded);
        CollectionAssert.AreEqual(Convert.FromHexString(
            "4C007B00000000002400" +
            "3F02030003000000" +
            "0000C84200004843" +
            "000080BF0000803F" +
            "0000003FE80300000000803E"), encoded);

        RefBinaryReader reader = new(encoded);
        WatchEntityState decoded = reader.Read<WatchEntityState>();
        Assert.AreEqual(0, reader.BytesLeft);
        Assert.AreEqual(state.Key, decoded.Key);
        CollectionAssert.AreEqual(state.Payload.ToArray(), decoded.Payload.ToArray());
    }

    [TestMethod]
    [DataRow(0, 0f)]
    [DataRow(32, 0.5f)]
    [DataRow(36, 1f)]
    public void CompactStateRetainsAbsentHiddenAndVisibleLightStates(int flags, float alpha)
    {
        WatchEntityState state = CreateState(payload =>
        {
            payload[0] = (byte)flags;
            BitConverter.TryWriteBytes(payload.AsSpan(24), alpha);
        });
        Assert.IsTrue(WatchPacketValidator.IsValid(state));
        RefBinaryReader reader = new(Encode(state));
        WatchEntityState decoded = reader.Read<WatchEntityState>();
        Assert.AreEqual((byte)flags, decoded.Payload.Span[0]);
        Assert.AreEqual(alpha, BitConverter.ToSingle(decoded.Payload.Span[24..]));
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(12)]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(24)]
    [DataRow(32)]
    public void CompactStateStillRejectsNonFiniteFields(int offset)
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.IsFalse(WatchPacketValidator.IsValid(CreateState(payload =>
                BitConverter.TryWriteBytes(payload.AsSpan(offset), value))));
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public void CompactStateKeepsReservedBytesZero(int offset)
        => Assert.IsFalse(WatchPacketValidator.IsValid(CreateState(payload => payload[offset] = 1)));

    [TestMethod]
    public void CompactSnapshotAndDeathReplaceUseCachedEncodingWithoutExtensions()
    {
        WatchEntityState state = CreateState();
        WatchSceneSnapshot snapshot = new(Location, 7, [], [state], 9, 11);
        WatchSceneDelta delta = new(8, Location, [], [], false,
            WatchEntityStateMode.Replace, [state], [], isDeathRespawn: true,
            playerEpoch: 9, playerSequenceWatermark: 12);
        Assert.IsTrue(WatchPacketValidator.IsValid(snapshot));
        Assert.IsTrue(WatchPacketValidator.IsValid(delta));
        CollectionAssert.AreEqual(Encode(snapshot), snapshot.EncodedPayload.ToArray());
        CollectionAssert.AreEqual(Encode(delta), delta.EncodedPayload.ToArray());

        RefBinaryReader snapshotReader = new(snapshot.EncodedPayload.Span);
        WatchSceneSnapshot decodedSnapshot = snapshotReader.Read<WatchSceneSnapshot>();
        Assert.AreEqual(0, snapshotReader.BytesLeft);
        RefBinaryReader deltaReader = new(delta.EncodedPayload.Span);
        WatchSceneDelta decodedDelta = deltaReader.Read<WatchSceneDelta>();
        Assert.AreEqual(0, deltaReader.BytesLeft);
        Assert.IsTrue(decodedDelta.IsDeathRespawn);
        Assert.AreEqual(WatchEntityStateMode.Replace, decodedDelta.EntityStateMode);
        Assert.AreEqual(36, decodedSnapshot.EntityStates.Single().Payload.Length);
        Assert.AreEqual(36, decodedDelta.EntityStates.Single().Payload.Length);
        CollectionAssert.AreEqual(state.Payload.ToArray(), decodedDelta.EntityStates.Single().Payload.ToArray());
    }

    private static WatchEntityState CreateState(Action<byte[]>? mutate = null)
    {
        byte[] payload = new byte[36];
        payload[0] = 0b0011_1111;
        payload[1] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), 3);
        payload[4] = (byte)WatchNarrativeNPCVisual.Oshiro;
        BitConverter.TryWriteBytes(payload.AsSpan(8), 100f);
        BitConverter.TryWriteBytes(payload.AsSpan(12), 200f);
        BitConverter.TryWriteBytes(payload.AsSpan(16), -1f);
        BitConverter.TryWriteBytes(payload.AsSpan(20), 1f);
        BitConverter.TryWriteBytes(payload.AsSpan(24), 0.5f);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28), 1000);
        BitConverter.TryWriteBytes(payload.AsSpan(32), 0.25f);
        mutate?.Invoke(payload);
        return new(new(WatchEntityKind.NarrativeNPC, 123), payload);
    }

    private static byte[] Encode<T>(T value) where T : IRefBinarySerializable<T>
    {
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        writer.Write(value);
        return stream.ToArray();
    }
}
