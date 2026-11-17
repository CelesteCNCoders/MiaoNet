using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public static class CommandSegmentResolver
{
    public static IEnumerable<OnlinePlayer> ResolvePlayers(ClientState state, PlayerScope scope)
        => scope switch
        {
            PlayerScope.Any => state.Players.Values,
            PlayerScope.SameChannel => state.SelfChannel.Players,
            PlayerScope.SameMap => state.SelfChannel.Players.Where(p => p.ShouldSyncFrom(state.Self)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };

    public static IEnumerable<OnlineChannel> ResolveChannels(ClientState state)
        => state.Channels.Values.Where(c => c.ID != ChannelInfo.PrivateChannelVirtualID && !c.IsPrivate);
}
