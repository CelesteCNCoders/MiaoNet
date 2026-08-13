using System.Collections.Concurrent;

namespace MiaoNet.Server;

/// <summary>
/// In-memory store of temporarily frozen accounts (keyed by forum auth id).
/// Frozen accounts are rejected at handshake until the freeze expires.
/// </summary>
public sealed class TemporaryFreezeStore
{
    private readonly ConcurrentDictionary<int, DateTimeOffset> frozenUntilByAuthID = new();

    public void Freeze(int authID, TimeSpan duration)
        => frozenUntilByAuthID[authID] = DateTimeOffset.UtcNow + duration;

    public bool TryGetFrozenUntil(int authID, out DateTimeOffset until)
    {
        if (frozenUntilByAuthID.TryGetValue(authID, out until))
        {
            if (until > DateTimeOffset.UtcNow)
                return true;
            // expired, drop it
            frozenUntilByAuthID.TryRemove(authID, out _);
        }
        until = default;
        return false;
    }
}
