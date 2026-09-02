namespace MiaoNet.Shared;

[Flags]
public enum ServerFeatureFlags : ushort
{
    None = 0,
    WatchSceneSync = 1 << 0,
    WatchRestartContinuation = 1 << 1,
}

public static class WatchProtocolCompatibility
{
    public static bool SupportsWatchSceneSync(
        ServerFeatureFlags serverFeatures,
        PlayerGlobalFlags clientFlags
    ) => serverFeatures.HasFlag(ServerFeatureFlags.WatchSceneSync)
        && clientFlags.HasFlag(PlayerGlobalFlags.WatchSceneSyncSupported);

    public static bool SupportsWatchRestartContinuation(
        ServerFeatureFlags serverFeatures,
        PlayerGlobalFlags clientFlags
    ) => serverFeatures.HasFlag(ServerFeatureFlags.WatchRestartContinuation)
        && clientFlags.HasFlag(PlayerGlobalFlags.WatchRestartContinuationSupported);

    public static bool CanUseWatchSceneSync(
        ServerFeatureFlags serverFeatures,
        PlayerGlobalFlags watcherFlags,
        PlayerGlobalFlags targetFlags
    ) => SupportsWatchSceneSync(serverFeatures, watcherFlags)
        && SupportsWatchSceneSync(serverFeatures, targetFlags);
}
