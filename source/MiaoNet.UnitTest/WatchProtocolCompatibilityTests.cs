using System.Reflection;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchProtocolCompatibilityTests
{
    [TestMethod]
    public void RepurposedAndRetiredEntityKindsKeepTheirWireValues()
    {
        Assert.AreEqual(
            (ushort)23,
            (ushort)Enum.Parse<WatchEntityKind>(nameof(WatchEntityKind.TouchSwitchAndSwitchGate))
        );
        Assert.AreEqual(
            (ushort)37,
            (ushort)Enum.Parse<WatchEntityKind>(nameof(WatchEntityKind.Reserved37))
        );
    }

    [TestMethod]
    public void WatchPacketsAreAppendedAfterUpstreamPackets()
    {
        PacketRegistryAttribute registry = typeof(PacketRegistry).Assembly
            .GetCustomAttribute<PacketRegistryAttribute>()!;
        Type[] expectedWatchPackets =
        [
            typeof(PacketWatchStart),
            typeof(PacketWatchStartResponse),
            typeof(PacketWatchSnapshotRequest),
            typeof(PacketWatchSnapshotResponse),
            typeof(PacketWatchSceneDelta),
            typeof(PacketWatchSceneDeltaNotification),
            typeof(PacketWatchStop),
            typeof(PacketWatchProducerStop),
            typeof(PacketWatchEnded),
            typeof(PacketWatchResyncRequest),
            typeof(PacketWatchResyncSnapshot),
            typeof(PacketWatchSceneTransferStart),
            typeof(PacketWatchSceneChunk),
            typeof(PacketWatchSceneCancel),
            typeof(PacketWatchTargetRestarting),
            typeof(PacketWatchTargetRestartingNotification),
        ];
        int lastUpstreamPacket = Array.IndexOf(registry.Types, typeof(PacketChannelCreated));

        Assert.IsGreaterThanOrEqualTo(0, lastUpstreamPacket);
        CollectionAssert.AreEqual(
            expectedWatchPackets,
            registry.Types[(lastUpstreamPacket + 1)..]
        );
    }

    [TestMethod]
    public void PacketUpdateGlobalFlagPreservesUShortFlags()
    {
        PlayerGlobalFlags expected = PlayerGlobalFlags.Watching
            | PlayerGlobalFlags.WatchSceneSyncSupported
            | PlayerGlobalFlags.WatchRestartContinuationSupported;
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        new PacketUpdateGlobalFlag(expected).Serialize(ref writer);

        Assert.AreEqual(sizeof(ushort), stream.Length);
        RefBinaryReader legacyReader = new(stream.ToArray());
        Assert.AreEqual((byte)expected, legacyReader.ReadByte());
        Assert.AreEqual(1, legacyReader.BytesLeft);

        RefBinaryReader reader = new(stream.ToArray());
        PacketUpdateGlobalFlag actual = PacketUpdateGlobalFlag.Deserialize(ref reader);
        Assert.AreEqual(expected, actual.Flags);
        Assert.AreEqual(0, reader.BytesLeft);
    }

    [TestMethod]
    public void PacketClientInitialReadsLegacyPayloadWithoutServerFeatures()
    {
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        writer.Write(1);
        writer.Write(2);
        writer.Write(new PlayerInfo(3, "name", "prefix", string.Empty, Color.White));
        writer.Write(Array.Empty<PacketClientInitial.Channel>());
        writer.Write(Array.Empty<PacketClientInitial.Player>());
        writer.Write(new PlayerPresenceMessage("joined", "left"));
        writer.Write("hello");

        RefBinaryReader reader = new(stream.ToArray());
        PacketClientInitial packet = PacketClientInitial.Deserialize(ref reader);

        Assert.AreEqual(ServerFeatureFlags.None, packet.ServerFeatures);
        Assert.AreEqual(0, reader.BytesLeft);
    }

    [TestMethod]
    public void PacketClientInitialRoundTripsServerFeaturesAtPayloadTail()
    {
        PacketClientInitial expected = new(
            1,
            2,
            new PlayerInfo(3, "name", "prefix", string.Empty, Color.White),
            Array.Empty<PacketClientInitial.Channel>(),
            Array.Empty<PacketClientInitial.Player>(),
            new PlayerPresenceMessage("joined", "left"),
            "hello",
            ServerFeatureFlags.WatchSceneSync
                | ServerFeatureFlags.WatchRestartContinuation
        );
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        expected.Serialize(ref writer);

        RefBinaryReader legacyReader = new(stream.ToArray());
        legacyReader.ReadInt32();
        legacyReader.ReadInt32();
        legacyReader.Read<PlayerInfo>();
        legacyReader.ReadArray<PacketClientInitial.Channel>();
        legacyReader.ReadArray<PacketClientInitial.Player>();
        legacyReader.Read<PlayerPresenceMessage>();
        legacyReader.ReadString();
        Assert.AreEqual(sizeof(ushort), legacyReader.BytesLeft);

        RefBinaryReader reader = new(stream.ToArray());
        PacketClientInitial actual = PacketClientInitial.Deserialize(ref reader);

        Assert.AreEqual(
            ServerFeatureFlags.WatchSceneSync
                | ServerFeatureFlags.WatchRestartContinuation,
            actual.ServerFeatures
        );
        Assert.AreEqual(0, reader.BytesLeft);
    }

    [TestMethod]
    public void PacketClientInitialRoundTripsPlayerTimelinePosition()
    {
        PacketClientInitial expected = new(
            1,
            2,
            new PlayerInfo(3, "self", string.Empty, string.Empty, Color.White),
            [new PacketClientInitial.Channel(1, new ChannelInfo("main"))],
            [new PacketClientInitial.Player(
                1,
                4,
                new PlayerInfo(5, "remote", string.Empty, string.Empty, Color.White),
                new PlayerLocation("Celeste/1-ForsakenCity", AreaMode.Normal, "1"),
                9,
                27,
                PlayerGlobalFlags.None
            )],
            new PlayerPresenceMessage("joined", "left"),
            "hello"
        );
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        expected.Serialize(ref writer);

        RefBinaryReader reader = new(stream.ToArray());
        PacketClientInitial actual = PacketClientInitial.Deserialize(ref reader);

        PacketClientInitial.Player player = actual.Players.Single();
        Assert.AreEqual((uint)9, player.PlayerEpoch);
        Assert.AreEqual((uint)27, player.PlayerSequence);
        Assert.AreEqual(0, reader.BytesLeft);
    }

    [TestMethod]
    public void SceneSyncRequiresServerWatcherAndTargetSupport()
    {
        const PlayerGlobalFlags supported = PlayerGlobalFlags.WatchSceneSyncSupported;
        const ServerFeatureFlags server = ServerFeatureFlags.WatchSceneSync;

        Assert.IsTrue(WatchProtocolCompatibility.CanUseWatchSceneSync(
            server,
            supported,
            supported
        ));
        Assert.IsFalse(WatchProtocolCompatibility.CanUseWatchSceneSync(
            ServerFeatureFlags.None,
            supported,
            supported
        ));
        Assert.IsFalse(WatchProtocolCompatibility.CanUseWatchSceneSync(
            server,
            PlayerGlobalFlags.None,
            supported
        ));
        Assert.IsFalse(WatchProtocolCompatibility.CanUseWatchSceneSync(
            server,
            supported,
            PlayerGlobalFlags.None
        ));
    }

    [TestMethod]
    public void RestartContinuationRequiresItsServerAndClientCapabilityBits()
    {
        const PlayerGlobalFlags client =
            PlayerGlobalFlags.WatchRestartContinuationSupported;
        const ServerFeatureFlags server = ServerFeatureFlags.WatchRestartContinuation;

        Assert.IsTrue(WatchProtocolCompatibility.SupportsWatchRestartContinuation(
            server,
            client
        ));
        Assert.IsFalse(WatchProtocolCompatibility.SupportsWatchRestartContinuation(
            ServerFeatureFlags.None,
            client
        ));
        Assert.IsFalse(WatchProtocolCompatibility.SupportsWatchRestartContinuation(
            server,
            PlayerGlobalFlags.None
        ));
    }
}
