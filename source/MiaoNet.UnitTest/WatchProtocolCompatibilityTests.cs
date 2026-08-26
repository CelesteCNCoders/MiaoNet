using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class WatchProtocolCompatibilityTests
{
    [TestMethod]
    public void PacketUpdateGlobalFlagPreservesUShortFlags()
    {
        PlayerGlobalFlags expected = PlayerGlobalFlags.Watching
            | PlayerGlobalFlags.WatchSceneSyncSupported;
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

        Assert.AreEqual(ServerFeatureFlags.WatchSceneSync, actual.ServerFeatures);
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
}
