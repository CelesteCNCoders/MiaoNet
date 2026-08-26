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

    internal static bool HasValidCameraPosition(PlayerState state)
        => state.CameraPosition is not { } position
            || IsFinite(position);

    internal static bool HasValidCameraPosition(PlayerStateDelta delta)
        => !delta.HasCameraPosition
            || IsFinite(delta.CameraPosition);

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
