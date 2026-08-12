using MiaoNet.Shared;

namespace MiaoNet.Server;

internal static class PlayerPacketValidator
{
    internal const int MaxFollowersCount = 12;

    internal static bool HasValidFollowerCount(PlayerState state)
        => state.FollowerInfos is not null
            && state.FollowerInfos.Length <= MaxFollowersCount;

    internal static bool HasValidFollowerCount(PlayerStateDelta delta)
    {
        int count = delta.FollowerInitials is not null
            ? delta.FollowerInitials.Length
            : delta.FollowerDeltas is not null
                ? delta.FollowerDeltas.Length
                : 0;

        return count <= MaxFollowersCount;
    }
}
