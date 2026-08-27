using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchSceneTransferTests
{
    private static readonly PlayerLocation Location = new(
        "Celeste/1-ForsakenCity",
        AreaMode.Normal,
        "1"
    );

    [TestMethod]
    public void LargeSnapshotIsReassembledBeforeLogicalPacketIsPublished()
    {
        WatchSceneSnapshot snapshot = CreateLargeSnapshot();
        PacketWatchStartResponse packet = new(
            WatchStartResult.Success,
            44,
            snapshot,
            55
        ) { RequestID = 19 };

        Assert.IsTrue(WatchSceneFragmenter.TryFragment(packet, out var fragments));
        Assert.IsGreaterThan(2, fragments.Count);
        WatchSceneTransferReceiver receiver = new();

        Assert.IsTrue(receiver.TryAccept(fragments[0], out IContextualPacket? logical));
        Assert.IsNull(logical);
        for (int index = fragments.Count - 1; index >= 1; index--)
        {
            Assert.IsTrue(receiver.TryAccept(fragments[index], out logical));
            if (index != 1)
                Assert.IsNull(logical);
        }

        PacketWatchStartResponse rebuilt = (PacketWatchStartResponse)logical!;
        Assert.AreEqual(19, rebuilt.RequestID);
        Assert.AreEqual(44, rebuilt.SessionID);
        Assert.AreEqual(55, rebuilt.TargetPlayerID);
        Assert.AreEqual(snapshot.Sequence, rebuilt.Snapshot!.Sequence);
        Assert.AreEqual(snapshot.PlayerEpoch, rebuilt.Snapshot.PlayerEpoch);
        Assert.AreEqual(snapshot.PlayerSequenceWatermark, rebuilt.Snapshot.PlayerSequenceWatermark);
        Assert.HasCount(snapshot.EntityStates.Count, rebuilt.Snapshot.EntityStates);
    }

    [TestMethod]
    public void DuplicateFragmentIsRejectedWithoutPublishingPartialState()
    {
        PacketWatchSceneDelta packet = new(CreateLargeDelta());
        Assert.IsTrue(WatchSceneFragmenter.TryFragment(packet, out var fragments));
        WatchSceneTransferReceiver receiver = new();
        receiver.TryAccept(fragments[0], out _);
        receiver.TryAccept(fragments[1], out IContextualPacket? logical);
        Assert.IsNull(logical);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            receiver.TryAccept(fragments[1], out _)
        );
    }

    [TestMethod]
    public void FragmentLengthMismatchIsRejected()
    {
        PacketWatchSceneDelta packet = new(CreateLargeDelta());
        Assert.IsTrue(WatchSceneFragmenter.TryFragment(packet, out var fragments));
        PacketWatchSceneChunk original = (PacketWatchSceneChunk)fragments[1];
        PacketWatchSceneChunk truncated = new(
            original.TransferID,
            original.FragmentIndex,
            original.Data[..^1]
        );
        WatchSceneTransferReceiver receiver = new();
        receiver.TryAccept(fragments[0], out _);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            receiver.TryAccept(truncated, out _)
        );
    }

    private static WatchSceneSnapshot CreateLargeSnapshot()
        => new(Location, 7, [], CreateStates(), 12, 34);

    private static WatchSceneDelta CreateLargeDelta()
        => new(
            8,
            Location,
            [],
            [],
            false,
            WatchEntityStateMode.Replace,
            CreateStates(),
            [],
            playerEpoch: 12,
            playerSequenceWatermark: 35
        );

    private static WatchEntityState[] CreateStates()
        => Enumerable.Range(0, 10)
            .Select(index => new WatchEntityState(
                new WatchEntityKey(WatchEntityKind.CrumblePlatform, index),
                CreateCrumblePayload()
            ))
            .ToArray();

    private static byte[] CreateCrumblePayload()
    {
        byte[] payload = new byte[WatchPacketValidator.MaxEntityPayloadBytes];
        int imageCount = (payload.Length - 4) * 8;
        payload[2] = (byte)imageCount;
        payload[3] = (byte)(imageCount >> 8);
        return payload;
    }
}
