using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal static class PlayerRespawnTimeline
{
    internal static bool CanEmitRespawn(
        PlayerLocation synchronizedLocation,
        PlayerLocation respawnLocation,
        PlayerStateFlags? stateFlags
    ) => stateFlags?.HasFlag(PlayerStateFlags.Dead) == true
        && synchronizedLocation == respawnLocation;
}
