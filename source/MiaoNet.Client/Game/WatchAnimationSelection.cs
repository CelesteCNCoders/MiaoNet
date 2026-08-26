namespace Celeste.Mod.MiaoNet;

internal static class WatchAnimationSelection
{
    public static string? Select(
        string requested,
        string? current,
        Func<string, bool> isAvailable
    )
    {
        if (isAvailable(requested))
            return requested;
        if (!string.IsNullOrEmpty(current) && isAvailable(current))
            return current;
        return isAvailable("idle") ? "idle" : null;
    }
}
