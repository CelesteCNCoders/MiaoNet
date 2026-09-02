namespace Celeste.Mod.MiaoNet;

internal static class WatchLockBlockTiming
{
    internal const float KeyTravelDuration = 1f;
    internal const float RegisterUsedDelay = 1.2f;
    internal const float InsertPauseDuration = 0.3f;
    internal const float KeyTurnDuration = 0.3f;
    internal const float FinishPauseDuration = 0.2f;

    internal const float MinimumKeyUseDuration =
        KeyTravelDuration
        + InsertPauseDuration
        + KeyTurnDuration
        + FinishPauseDuration;
}
