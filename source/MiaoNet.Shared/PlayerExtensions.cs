#if MIAO_SERVER
using MiaoNet.Server;
using MiaoNet.Server.GameScope;
#elif MIAO_CLIENT
using Celeste.Mod.MiaoNet;
#endif

namespace MiaoNet.Shared;

#if true
public static class PlayerExtensions
{
    /// <summary>this &lt;--- other</summary>
#if MIAO_SERVER
    public static bool ShouldSyncFrom(this ServerPlayer player, ServerPlayer other)
    {
        if (other.Location.IsInDebugMap)
            return false;

        return player.Scope is MapScope && player.Scope == other.Scope;
    }

    public static bool PlayerShouldSyncFrom(this MiaoClientConnection connection, MiaoClientConnection other)
        => connection.Player.ShouldSyncFrom(other.Player);
#elif MIAO_CLIENT
    public static bool ShouldSyncFrom(this OnlinePlayer player, OnlinePlayer other)
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
#endif
}
#endif