using MiaoNet.Shared;

namespace MiaoNet.Server;

internal static class PlayerInteractionValidator
{
    internal static bool CanGrab(ServerPlayer source, ServerPlayer target)
        => target.ShouldSyncFrom(source)
            && target.GlobalFlags.HasFlag(PlayerGlobalFlags.Interactions)
            && source.GlobalFlags.HasFlag(PlayerGlobalFlags.Interactions);

    internal static bool IsValidReleaseForce(Vector2 force)
        => float.IsFinite(force.X) && float.IsFinite(force.Y);
}
