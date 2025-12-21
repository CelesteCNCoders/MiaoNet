namespace MiaoNet.Shared;

public static partial class KnownPooledStrings
{
    public static IEnumerable<string> All =>
        PlayerAnimations.Prepend(string.Empty);
}