#if MIAO_SERVER
using MiaoNet.Server;
#elif MIAO_CLIENT
using Celeste.Mod.MiaoNet;
#endif

namespace MiaoNet.Shared;

// TODO avoid using these server-side
#if true
public static class PlayerExtensions
{
    /// <summary>this &lt;--- other</summary>
#if MIAO_SERVER
    public static bool ShouldSyncFrom(this ServerPlayer player, ServerPlayer other)
#elif MIAO_CLIENT
    public static bool ShouldSyncFrom(this OnlinePlayer player, OnlinePlayer other)
#endif
    {
        if (player.ChannelId != other.ChannelId)
            return false;

        if (player.Location.IsEmpty)
            return false;

        if (other.Location.IsInDebugMap)
            return false;

        if (player.Location.Map != other.Location.Map)
            return false;

        return true;
    }

#if MIAO_SERVER
    public static bool PlayerShouldSyncFrom(this MiaoClientConnection connection, MiaoClientConnection other)
        => connection.Player.ShouldSyncFrom(other.Player);
#endif
}
#endif