using MiaoNet.Shared;

namespace MiaoNet.Server;

internal static class PlayerFrameRouting
{
    internal static bool IsActiveWatcher(
        IReadOnlyCollection<WatchSession> targetSessions,
        int playerID,
        PlayerMapLocation map
    )
    {
        foreach (WatchSession session in targetSessions)
        {
            if (session.IsActive && session.WatcherID == playerID && session.Map == map)
                return true;
        }

        return false;
    }

    internal static PacketPlayerFrame CreateWithoutCamera(PacketPlayerFrame packet)
    {
        PlayerStateDelta source = packet.StateDelta;
        if (!source.HasCameraPosition)
            return packet;

        PlayerStateDelta stripped = new(
            source.Position,
            source.Animation,
            source.AnimationFrame,
            source.Scale,
            source.Flags & ~PlayerStateDelta.FrameFlags.HasCameraPosition,
            source.StateFlags
        )
        {
            Dashes = source.Dashes,
            DashDirection = source.DashDirection,
            HoldableInfo = source.HoldableInfo,
            FollowerInitials = source.FollowerInitials,
            FollowerDeltas = source.FollowerDeltas,
            WindDirection = source.WindDirection,
        };
        return new(stripped);
    }
}
