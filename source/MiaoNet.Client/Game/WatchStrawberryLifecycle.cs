using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal static class WatchStrawberryLifecycle
{
    private const string StrawberryEntityName = "strawberry";
    private const string GoldenBerryEntityName = "goldenBerry";

    internal static bool IsTrackedMapEntity(string name, bool winged)
        => IsGoldenBerry(name)
            || StringComparer.Ordinal.Equals(name, StrawberryEntityName) && winged;

    internal static bool IsGoldenBerry(string name)
        => StringComparer.Ordinal.Equals(name, GoldenBerryEntityName);

    internal static bool SupportsFlyingAway(string name, bool winged)
        => StringComparer.Ordinal.Equals(name, StrawberryEntityName) && winged;

    internal static bool IsValidState(
        string name,
        bool winged,
        WatchWingedStrawberryState state
    ) => IsTrackedMapEntity(name, winged)
        && (state != WatchWingedStrawberryState.FlyingAway
            || SupportsFlyingAway(name, winged));
}
