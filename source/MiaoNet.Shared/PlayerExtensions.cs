#if MIAO_SERVER
using MiaoNet.Server;
#elif MIAO_CLIENT
using Celeste.Mod.MiaoNet;
#endif

namespace MiaoNet.Shared;

public static class PlayerExtensions
{
    /// <summary>this &lt;--- other</summary>
#if MIAO_SERVER
    public static bool ShouldSyncFrom(this ServerPlayer player, ServerPlayer other)
#elif MIAO_CLIENT
    public static bool ShouldSyncFrom(this OnlinePlayer player, OnlinePlayer other)
#endif
    {
        if (player.Channel != other.Channel)
            return false;

        if (player.Location.IsEmpty)
            return false;

        if (other.Location.IsInDebugMap)
            return false;

        if (!player.Location.IsSameMapWith(other.Location))
            return false;

        return true;
    }

#if MIAO_SERVER
    public static bool PlayerShouldSyncFrom(this MiaoClientConnection connection, MiaoClientConnection other)
        => connection.Player.ShouldSyncFrom(other.Player);
#endif
}