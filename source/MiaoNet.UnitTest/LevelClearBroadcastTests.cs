using System.Reflection;
using System.Threading.Channels;
using MiaoNet.Server;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class LevelClearBroadcastTests
{
    [TestMethod]
    public async Task ShareBroadcastsToAchievementsChannelAcrossPlayerChannels()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var other = AddPlayer(server);
        var otherChannel = server.ServerState.CreateNewChannel(new ChannelInfo("other"));
        server.ServerState.AddChannel(otherChannel);
        server.ServerState.PlayerChannelMove(other, other.Player.Channel, otherChannel);

        var record = new LevelClearRecord { MapSID = "test/map", MapName = "Test Map", TimeTicks = 123, Deaths = 4 };
        await server.HandlePacketAsync(sender, default, new PacketShareLevelClear(record));

        foreach (var connection in new[] { sender, other })
        {
            Assert.IsTrue(Queue(connection).TryRead(out var enveloped));
            var packet = enveloped.Packet as PacketLevelClear;
            Assert.IsNotNull(packet);
            Assert.AreEqual(ServerChatChannel.AchievementsID, packet.ChatChannelID);
            Assert.AreEqual(sender.ID, packet.PlayerID);
            Assert.AreEqual(record, packet.Record);
            Assert.IsLessThan(TimeSpan.FromSeconds(5), (DateTime.UtcNow - packet.DateTime).Duration());
        }

        // Immediate repeat is ignored by cooldown/dedup.
        await server.HandlePacketAsync(sender, default, new PacketShareLevelClear(record));
        Assert.IsFalse(Queue(sender).TryRead(out _));
        Assert.IsFalse(Queue(other).TryRead(out _));
    }

    [TestMethod]
    public async Task InvalidRecordDisconnectsSenderWithoutBroadcast()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var other = AddPlayer(server);

        await server.HandlePacketAsync(sender, default, new PacketShareLevelClear(new LevelClearRecord()));

        Assert.IsTrue(Queue(sender).TryRead(out var enveloped));
        Assert.IsInstanceOfType<PacketDisconnected>(enveloped.Packet);
        Assert.IsFalse(Queue(other).TryRead(out _));
    }

    [TestMethod]
    public async Task ReconnectDoesNotRebroadcastSameClearButConfirmsToSender()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var observer = AddPlayer(server);
        var record = new LevelClearRecord { MapSID = "test/map", MapName = "Test Map", TimeTicks = null };

        await server.HandlePacketAsync(sender, default, new PacketShareLevelClear(record));
        Assert.IsTrue(Queue(sender).TryRead(out _));
        Assert.IsTrue(Queue(observer).TryRead(out _));

        server.ServerState.RemovePlayer(sender);
        var reconnected = AddPlayer(server);
        await server.HandlePacketAsync(reconnected, default, new PacketShareLevelClear(record));

        Assert.IsFalse(Queue(observer).TryRead(out _));
        Assert.IsTrue(Queue(reconnected).TryRead(out var ack));
        var clearAck = ack.Packet as PacketLevelClear;
        Assert.IsNotNull(clearAck);
        Assert.AreEqual(ServerChatChannel.AchievementsID, clearAck.ChatChannelID);
        Assert.AreEqual(reconnected.ID, clearAck.PlayerID);
        Assert.AreEqual(record.ClearID, clearAck.Record.ClearID);
    }

    private static MiaoServerService CreateServer() => new(
        NullLogger<MiaoServerService>.Instance,
        Options.Create(new MiaoServerOptions()),
        new TestAnnouncementsMonitor(),
        _ => null!,
        null!,
        null!,
        new MiaoMetricsService()
    );

    private static MiaoClientConnection AddPlayer(MiaoServerService server)
    {
        var player = server.ServerState.CreateNewPlayer(new PlayerInfo(1, "test", string.Empty, string.Empty, Color.White));
        var connection = new MiaoClientConnection(
            new DummyNetworkConnection(),
            player,
            NullLogger<MiaoClientConnection>.Instance,
            server,
            new MiaoMetricsService()
        );
        server.ServerState.AddPlayer(connection);
        return connection;
    }

    private static ChannelReader<EnvelopedPacket> Queue(MiaoClientConnection connection)
        => ((Channel<EnvelopedPacket>)typeof(MiaoClientConnection)
            .GetField("sendChannel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(connection)!).Reader;

    private sealed class TestAnnouncementsMonitor : IOptionsMonitor<AnnouncementsOptions>
    {
        public AnnouncementsOptions CurrentValue { get; } = new()
        {
            SChinese = new(string.Empty, string.Empty, string.Empty),
            English = new(string.Empty, string.Empty, string.Empty)
        };

        public AnnouncementsOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AnnouncementsOptions, string?> listener) => null;
    }

    private sealed class DummyNetworkConnection : INetworkConnection
    {
        public string RemoteAddress => "test";

        public Stream Stream { get; } = new MemoryStream();

        public void Shutdown() { }

        public void Dispose() => Stream.Dispose();
    }
}
