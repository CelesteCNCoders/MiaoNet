using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MiaoNet.Shared;

namespace MiaoNet.Server.GameScope;

public class ScopeTree
{
    private readonly GlobalScope root;
    private readonly ReaderWriterLockSlim treeLock;
    private ImmutableArray<Scope> scopes;
    private ImmutableHashSet<ServerPlayer> allPlayers;
    private readonly ILogger<ScopeTree> logger;

    public GlobalScope Root => root;

    public ImmutableHashSet<ServerPlayer> AllPlayers => allPlayers;

    public ScopeTree(ILogger<ScopeTree> logger)
    {
        this.logger = logger;
        root = new GlobalScope();
        scopes = ImmutableArray<Scope>.Empty.Add(root);
        treeLock = new();
        allPlayers = ImmutableHashSet<ServerPlayer>.Empty;
    }

    public MoveResult MovePlayerToChannel(ServerPlayer player, ServerChannel channel)
    {
        if (channel.Scope is null)
            AddChannel(channel);

        if (player.Scope is MapScope mapScope)
            return MovePlayerToMapInChannel(player, mapScope.Map, channel.Scope!);

        if (player.Scope is RoomScope roomScope)
        {
            // TODO: Use Room
        }

        return MovePlayer(player, channel.Scope!);
    }

    public MoveResult MovePlayerToMap(ServerPlayer player, PlayerMap map)
    {
        using (treeLock.AcquireWriteLock())
        {
            var channelScope = AncestorOf<ChannelScope>(player.Scope)
                ?? throw new InvalidOperationException($"Player {player} is not in a channel");

            var mapScope = channelScope.EnsureMapScope(map);

            var source = player.Scope;
            logger.LogDebug("MovePlayerToMap: {player} from {source} to {target}", player, source, mapScope);

            var previousPeers = source?.Players ?? ImmutableHashSet<ServerPlayer>.Empty;

            source?.RemoveConnection(player);
            mapScope.AddConnection(player);
            player.Scope = mapScope;

            var newPeers = mapScope.Players.Remove(player);

            if (source is not null)
                Cleanup(source);

            logger.LogDebug("MovePlayerToMap done: previousPeers={prev}, newPeers={new}", previousPeers.Count, newPeers.Count);
            return new MoveResult(previousPeers, newPeers);
        }
    }

    public MoveResult MovePlayerToMapInChannel(ServerPlayer player, PlayerMap map, ChannelScope channel)
    {
        using (treeLock.AcquireWriteLock())
        {
            var mapScope = channel.EnsureMapScope(map);

            var source = player.Scope;
            logger.LogDebug("MovePlayerToMapInChannel: {player} from {source} to {target}", player, source, mapScope);

            var previousPeers = source?.Players ?? ImmutableHashSet<ServerPlayer>.Empty;

            source?.RemoveConnection(player);
            mapScope.AddConnection(player);
            player.Scope = mapScope;

            var newPeers = mapScope.Players.Remove(player);

            if (source is not null)
                Cleanup(source);

            logger.LogDebug("MovePlayerToMapInChannel done: previousPeers={prev}, newPeers={new}", previousPeers.Count, newPeers.Count);
            return new MoveResult(previousPeers, newPeers);
        }
    }

    public MoveResult MovePlayer(ServerPlayer player, Scope target)
    {
        using (treeLock.AcquireWriteLock())
        {
            var source = player.Scope;
            logger.LogDebug("MovePlayer: {player} from {source} to {target}", player.Info, source, target);

            var previousPeers = source?.Players ?? ImmutableHashSet<ServerPlayer>.Empty;

            source?.RemoveConnection(player);

            target.AddConnection(player);
            player.Scope = target;

            var newPeers = target.Players.Remove(player);

            if (source is not null)
                Cleanup(source);

            logger.LogDebug("MovePlayer done: previousPeers={prev}, newPeers={new}", previousPeers.Count, newPeers.Count);
            return new MoveResult(previousPeers, newPeers);
        }
    }

    public void AddPlayer(ServerPlayer player, Scope target)
    {
        using (treeLock.AcquireWriteLock())
        {
            logger.LogDebug("AddPlayer: {player} to {target}", player.Info, target);
            target.AddConnection(player);
            player.Scope = target;
            ImmutableInterlocked.Update(ref allPlayers, (d, p) => d.Add(p), player);
        }
    }

    public void RemovePlayer(ServerPlayer player)
    {
        using (treeLock.AcquireWriteLock())
        {
            var scope = player.Scope;
            if (scope is null)
                return;

            logger.LogDebug("RemovePlayer: {player} from {scope}", player.Info, scope);
            scope.RemoveConnection(player);
            player.Scope = null!;
            ImmutableInterlocked.Update(ref allPlayers, (d, p) => d.Remove(p), player);

            Cleanup(scope);
        }
    }

    public void AddChannel(ServerChannel channel)
    {
        using (treeLock.AcquireWriteLock())
        {
            logger.LogDebug("AddChannel: {channel}", channel.Info.Name);
            var scope = new ChannelScope(channel, root, logger);
            ImmutableInterlocked.Update(ref scopes, (d, p) => d.Add(p), scope);
        }
    }

    // TODO: Use Room here
    public void AddRoom(string room, MapScope mapScope)
    {
        using (treeLock.AcquireWriteLock())
        {
            var scope = new RoomScope(room, mapScope);
        }
    }


    public Scope? ScopeOf(ServerPlayer player)
        => player.Scope;

    public ChannelScope? ChannelOf(ServerPlayer player)
        => AncestorOf<ChannelScope>(player.Scope);

    public MapScope? MapOf(ServerPlayer player)
        => AncestorOf<MapScope>(player.Scope);

    public RoomScope? RoomOf(ServerPlayer player)
        => player.Scope as RoomScope;

    private static T? AncestorOf<T>(Scope? scope) where T : Scope
    {
        while (scope is not null)
        {
            if (scope is T typed)
                return typed;
            scope = scope.Parent;
        }
        return null;
    }

    private void Cleanup(Scope? scope)
    {
        while (scope is not null && !scope.Permanent && scope.IsEmpty)
        {
            logger.LogDebug("Cleanup: removing empty scope {scope}", scope);
            var p = scope.Parent;
            p?.RemoveChild(scope);
            scope = p;
        }
    }
}
