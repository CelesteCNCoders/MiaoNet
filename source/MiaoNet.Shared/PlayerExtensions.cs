#if MIAO_SERVER
using MiaoNet.Server;
#elif MIAO_CLIENT
using Celeste.Mod.MiaoNet;
#endif

namespace MiaoNet.Shared;

public static class PlayerExtensions
{
#if MIAO_SERVER
    /// <summary>this &lt;--- other</summary>
    public static SyncLevel SyncLevelWith(this ServerPlayer player, ServerPlayer other)
#elif MIAO_CLIENT
    public static SyncLevel SyncLevelWith(this OnlinePlayer player, OnlinePlayer other)
#endif
    {
        if (player.Channel != other.Channel)
            return SyncLevel.L0;
        if (player.Location.IsEmpty)
            return SyncLevel.L0;
        if (!player.Location.IsSameMapWith(other.Location))
            return SyncLevel.L0;
        if (player.Location.IsInDebugMap && other.Location.IsInMap)
            return SyncLevel.L1;
        SafeGuard.Assert(player.Location == other.Location);
        return SyncLevel.L2;
    }
}