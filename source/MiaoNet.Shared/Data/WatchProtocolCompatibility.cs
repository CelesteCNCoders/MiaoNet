namespace MiaoNet.Shared;

[Flags]
public enum ServerFeatureFlags : ushort
{
    None = 0,
    WatchSceneSync = 1 << 0,
}

public static class WatchProtocolCompatibility
{
    public static bool SupportsWatchSceneSync(
        ServerFeatureFlags serverFeatures,
        PlayerGlobalFlags clientFlags
    ) => serverFeatures.HasFlag(ServerFeatureFlags.WatchSceneSync)
        && clientFlags.HasFlag(PlayerGlobalFlags.WatchSceneSyncSupported);

    public static bool CanUseWatchSceneSync(
        ServerFeatureFlags serverFeatures,
        PlayerGlobalFlags watcherFlags,
        PlayerGlobalFlags targetFlags
    ) => SupportsWatchSceneSync(serverFeatures, watcherFlags)
        && SupportsWatchSceneSync(serverFeatures, targetFlags);
}
