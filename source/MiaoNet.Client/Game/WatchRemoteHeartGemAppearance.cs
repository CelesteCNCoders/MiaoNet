namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRemoteHeartGemAppearance
{
    // An authoritative normal Heart is false, so presence must be tracked separately.
    internal bool HasState { get; private set; }
    internal bool IsGhost { get; private set; }

    internal void Apply(bool isGhost)
    {
        HasState = true;
        IsGhost = isGhost;
    }

    internal bool TryGet(out bool isGhost)
    {
        isGhost = IsGhost;
        return HasState;
    }

    internal static bool ResolveCapture(bool? liveHeartIsGhost, bool areaHeartCollected)
        => liveHeartIsGhost ?? areaHeartCollected;
}
