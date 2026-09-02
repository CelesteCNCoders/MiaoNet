using MiaoNet.Shared;

namespace MiaoNet.Server;

internal static class PlayerFrameRouting
{
    internal static bool IsActiveWatcher(
        WatchSessionRegistry sessions,
        int targetID,
        int playerID,
        PlayerMapLocation map
    )
    {
        // The caller holds the server state lock, just as for the former scan.
        return sessions.TryGetByWatcher(playerID, out WatchSession? session)
            && session is { IsActive: true, IsRestartSuspended: false }
            && session.TargetID == targetID
            && session.Map == map;
    }

    internal static PacketPlayerFrame CreateWithoutCamera(PacketPlayerFrame packet)
    {
        if (!packet.HasCameraPosition)
            return packet;

        if (packet.Kind == PlayerFrameKind.Keyframe)
            return new PacketPlayerFrame(
                packet.PlayerEpoch,
                packet.PlayerSequence,
                packet.KeyframeState!
            );

        PlayerStateDelta source = packet.StateDelta!;

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
        return new PacketPlayerFrame(
            packet.PlayerEpoch,
            packet.PlayerSequence,
            stripped,
            packet.CoalescingSourceState
        );
    }
}
