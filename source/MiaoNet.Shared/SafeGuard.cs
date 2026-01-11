#if MIAO_SERVER || (MIAO_CLIENT && !SAFE_GUARD) || MIAO_MOCKCLIENT
global using SafeGuard = System.Diagnostics.Debug;
#endif
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MiaoNet.Shared;

// Debug.Assert boom the game
// let's use this to avoid booming the game but entering ooops screen at least

#if SAFE_GUARD && MIAO_CLIENT
internal class SafeGuardAssertException : Exception
{
    public SafeGuardAssertException() { }
    public SafeGuardAssertException(string? message) : base(message) { }
}

internal static class SafeGuard
{
    public static void Assert(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string expr = ""
    )
    {
        if (!condition)
            throw new SafeGuardAssertException($"Assertion failed: {expr}");
    }
}
#endif