using MiaoNet.Server;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class MinecraftBridgeTests
{
    [TestMethod]
    public void VirtualPlayerIDsAreNegativeAndUnique()
    {
        ServerState state = new();
        ServerChannel channel = state.CreateNewChannel(new ChannelInfo("Minecraft"));
        state.AddChannel(channel);

        var p1 = state.CreateNewVirtualPlayer(new PlayerInfo(0, "Steve", "Minecraft", string.Empty, Color.White), channel);
        var p2 = state.CreateNewVirtualPlayer(new PlayerInfo(0, "Alex", "Minecraft", string.Empty, Color.White), channel);

        Assert.IsLessThan(0, p1.ID);
        Assert.IsLessThan(0, p2.ID);
        Assert.AreNotEqual(p1.ID, p2.ID);
    }

    [TestMethod]
    public void AddAndRemoveVirtualPlayer()
    {
        ServerState state = new();
        ServerChannel channel = state.CreateNewChannel(new ChannelInfo("Minecraft"));
        state.AddChannel(channel);

        var player = state.CreateNewVirtualPlayer(new PlayerInfo(0, "Steve", "Minecraft", string.Empty, Color.White), channel);
        state.AddVirtualPlayer(player);
        Assert.IsTrue(state.VirtualPlayers.ContainsKey(player.ID));

        Assert.IsTrue(state.RemoveVirtualPlayer(player.ID, out var removed));
        Assert.AreSame(player, removed);
        Assert.IsFalse(state.VirtualPlayers.ContainsKey(player.ID));
        Assert.IsFalse(state.RemoveVirtualPlayer(player.ID, out _));
    }

    [TestMethod]
    public void PersistentChannelSurvivesBecomingEmpty()
    {
        ServerState state = new();
        ServerChannel channel = state.CreateNewChannel(new ChannelInfo("Minecraft"));
        channel.IsPersistent = true;
        state.AddChannel(channel);

        // join and leave with a real (fake) connection: the channel must stay
        var connection = CreateConnection(state);
        state.AddPlayer(connection);
        state.PlayerChannelMove(connection, state.Channels[ChannelInfo.MainChannelID], channel);
        state.PlayerChannelMove(connection, channel, state.Channels[ChannelInfo.MainChannelID]);
        state.RemovePlayer(connection);

        Assert.IsTrue(state.Channels.ContainsKey(channel.ID));
    }

    [TestMethod]
    public void NonPersistentChannelIsRemovedWhenEmpty()
    {
        ServerState state = new();
        ServerChannel channel = state.CreateNewChannel(new ChannelInfo("temp"));
        state.AddChannel(channel);

        var connection = CreateConnection(state);
        state.AddPlayer(connection);
        state.PlayerChannelMove(connection, state.Channels[ChannelInfo.MainChannelID], channel);
        state.PlayerChannelMove(connection, channel, state.Channels[ChannelInfo.MainChannelID]);
        state.RemovePlayer(connection);

        Assert.IsFalse(state.Channels.ContainsKey(channel.ID));
    }

    [TestMethod]
    public void PlayerListDiff()
    {
        Dictionary<string, string> current = new()
        {
            ["Steve"] = "overworld",
            ["Alex"] = "the_nether",
            ["Gone"] = "overworld"
        };
        Dictionary<string, string> incoming = new()
        {
            ["Steve"] = "overworld",
            ["Alex"] = "the_end",
            ["New"] = "overworld"
        };

        MinecraftPlayerListDiffer.Diff(current, incoming, out var added, out var removed, out var moved);

        CollectionAssert.AreEquivalent(new[] { "New" }, added);
        CollectionAssert.AreEquivalent(new[] { "Gone" }, removed);
        CollectionAssert.AreEquivalent(new[] { "Alex" }, moved);
    }

    [TestMethod]
    public void PlayerListDiffEmptyToEmpty()
    {
        MinecraftPlayerListDiffer.Diff(
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            out var added, out var removed, out var moved
        );
        Assert.HasCount(0, added);
        Assert.HasCount(0, removed);
        Assert.HasCount(0, moved);
    }

    private static MiaoClientConnection CreateConnection(ServerState state)
    {
        ServerPlayer player = state.CreateNewPlayer(new PlayerInfo(1, "test", string.Empty, string.Empty, Color.White));
        return new MiaoClientConnection(
            new FakeNetworkConnection(),
            player,
            NullLogger<MiaoClientConnection>.Instance,
            null!,
            new MiaoMetricsService()
        );
    }

    private sealed class FakeNetworkConnection : INetworkConnection
    {
        public string RemoteAddress => "test";
        public Stream Stream { get; } = new MemoryStream();
        public void Shutdown() { }
        public void Dispose() => Stream.Dispose();
    }
}
