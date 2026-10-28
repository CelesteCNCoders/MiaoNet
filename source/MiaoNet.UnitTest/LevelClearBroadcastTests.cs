// 不启动监听器，直接验证服务端消息处理的广播范围、身份和拒绝路径。
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
    public async Task ShareReachesSenderAndOtherChannelsWithServerIdentity()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var other = AddPlayer(server);
        var channel = server.ServerState.CreateNewChannel(new ChannelInfo("other"));
        server.ServerState.AddChannel(channel);
        server.ServerState.PlayerChannelMove(other, other.Player.Channel, channel);
        var record = new LevelClearRecord { MapSID = "test/map", MapName = "地图", TimeTicks = 123, Deaths = 4 };

        await server.HandlePacketAsync(sender, new PacketShareLevelClear(record));

        foreach (var connection in new[] { sender, other })
        {
            Assert.IsTrue(Queue(connection).TryRead(out var message));
            var packet = message as PacketLevelClear;
            Assert.IsNotNull(packet);
            Assert.AreEqual(sender.ID, packet.PlayerID);
            Assert.AreEqual(record, packet.Record);
            Assert.IsTrue((DateTime.UtcNow - packet.DateTime).Duration() < TimeSpan.FromSeconds(5));
        }

        await server.HandlePacketAsync(sender, new PacketShareLevelClear(record));
        Assert.IsFalse(Queue(sender).TryRead(out _));
        Assert.IsFalse(Queue(other).TryRead(out _));
    }

    [TestMethod]
    public async Task InvalidRecordIsNotBroadcast()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var other = AddPlayer(server);
        await server.HandlePacketAsync(sender, new PacketShareLevelClear(new LevelClearRecord()));
        Assert.IsTrue(Queue(sender).TryRead(out var message));
        Assert.IsInstanceOfType<PacketDisconnected>(message);
        Assert.IsFalse(Queue(other).TryRead(out _));
    }

    [TestMethod]
    public async Task ReconnectDoesNotRebroadcastSameClearButConfirmsToSender()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var observer = AddPlayer(server);
        var record = new LevelClearRecord { MapSID = "test", MapName = "地图" };
        await server.HandlePacketAsync(sender, new PacketShareLevelClear(record));
        Assert.IsTrue(Queue(sender).TryRead(out _));
        Assert.IsTrue(Queue(observer).TryRead(out _));

        server.ServerState.RemovePlayer(sender);
        var reconnected = AddPlayer(server);
        await server.HandlePacketAsync(reconnected, new PacketShareLevelClear(record));
        Assert.IsFalse(Queue(observer).TryRead(out _));
        Assert.IsTrue(Queue(reconnected).TryRead(out var ack));
        Assert.IsInstanceOfType<PacketLevelClear>(ack);
        Assert.AreEqual(reconnected.ID, ((PacketLevelClear)ack).PlayerID);
        Assert.AreEqual(record.ClearID, ((PacketLevelClear)ack).Record.ClearID);
    }

    [TestMethod]
    public async Task AnotherClearWithIdenticalStatsCanBeShared()
    {
        using var server = CreateServer();
        var sender = AddPlayer(server);
        var observer = AddPlayer(server);
        var first = new LevelClearRecord { MapSID = "test", MapName = "地图" };
        await server.HandlePacketAsync(sender, new PacketShareLevelClear(first));
        Assert.IsTrue(Queue(sender).TryRead(out _));
        Assert.IsTrue(Queue(observer).TryRead(out _));
        server.ServerState.RemovePlayer(sender);
        var reconnected = AddPlayer(server);
        var next = first with { ClearID = Guid.NewGuid().ToString("N") };
        await server.HandlePacketAsync(reconnected, new PacketShareLevelClear(next));
        Assert.IsTrue(Queue(observer).TryRead(out var message));
        Assert.AreEqual(next.ClearID, ((PacketLevelClear)message!).Record.ClearID);
    }

    private static MiaoServerService CreateServer() => new(
        NullLogger<MiaoServerService>.Instance, Options.Create(new MiaoServerOptions()),
        new Monitor(), _ => null!, null!, null!, new MiaoMetricsService(), new AdminChatBuffer(), new TemporaryFreezeStore());

    private static MiaoClientConnection AddPlayer(MiaoServerService server)
    {
        var player = server.ServerState.CreateNewPlayer(new PlayerInfo(1, "test", "", "", Color.White));
        var connection = new MiaoClientConnection(new Network(), player, NullLogger<MiaoClientConnection>.Instance,
            server, new MiaoMetricsService());
        server.ServerState.AddPlayer(connection);
        return connection;
    }

    private static ChannelReader<IContextualPacket> Queue(MiaoClientConnection connection)
        => ((Channel<IContextualPacket>)typeof(MiaoClientConnection)
            .GetField("sendChannel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!).Reader;

    private sealed class Monitor : IOptionsMonitor<AnnouncementsOptions>
    {
        public AnnouncementsOptions CurrentValue { get; } = new()
        {
            SChinese = new("", "", ""), English = new("", "", "")
        };
        public AnnouncementsOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AnnouncementsOptions, string?> listener) => null;
    }

    private sealed class Network : INetworkConnection
    {
        public string RemoteAddress => "test";
        public Stream Stream { get; } = new MemoryStream();
        public void Shutdown() { }
        public void Dispose() => Stream.Dispose();
    }
}
