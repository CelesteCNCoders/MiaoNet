using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public static class CommandSegmentResolver
{
    public static IEnumerable<OnlinePlayer> ResolvePlayers(ClientState state, CommandSegmentType segmentType)
        => segmentType switch
        {
            CommandSegmentType.Player => state.Players.Values,
            CommandSegmentType.PlayerSameChannel => state.SelfChannel.Players,
            CommandSegmentType.PlayerSameMap => state.SelfChannel.Players.Where(p => p.ShouldSyncFrom(state.Self)),
            _ => throw new ArgumentOutOfRangeException(nameof(segmentType), segmentType, null),
        };

    public static IEnumerable<OnlineChannel> ResolveChannels(ClientState state)
        => state.Channels.Values.Where(c => c.ID != ChannelInfo.PrivateChannelVirtualID && !c.IsPrivate);
}
