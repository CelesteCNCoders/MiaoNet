using System.Collections.Concurrent;

namespace MiaoNet.Server;

/// <summary>
/// Per-address guard against clients that reconnect over and over in a short
/// burst: too many connection attempts inside the window trigger a cooldown,
/// during which new connections from that address are refused immediately.
/// </summary>
public sealed class ConnectionCooldownTracker
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private const int MaxAttemptsPerWindow = 6;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(3);

    private sealed class Entry
    {
        public readonly Queue<DateTime> Attempts = new();
        public DateTime CooldownUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> entries = new();

    /// <summary>
    /// Records a connection attempt from <paramref name="remoteAddress"/> and
    /// tells whether the address is currently cooling down.
    /// </summary>
    public bool CheckAndRecord(string remoteAddress, out TimeSpan cooldownRemaining)
    {
        // RemoteAddress is "host:port"; the port changes on every reconnect, so key by host only
        string host = remoteAddress;
        int colon = remoteAddress.LastIndexOf(':');
        if (colon > 0)
            host = remoteAddress[..colon];
        var entry = entries.GetOrAdd(host, _ => new Entry());
        lock (entry)
        {
            DateTime now = DateTime.UtcNow;
            if (now < entry.CooldownUntil)
            {
                cooldownRemaining = entry.CooldownUntil - now;
                return true;
            }

            while (entry.Attempts.TryPeek(out DateTime oldest) && now - oldest > Window)
                entry.Attempts.Dequeue();
            entry.Attempts.Enqueue(now);

            if (entry.Attempts.Count > MaxAttemptsPerWindow)
            {
                entry.Attempts.Clear();
                entry.CooldownUntil = now + Cooldown;
                cooldownRemaining = Cooldown;
                return true;
            }

            cooldownRemaining = default;
            return false;
        }
    }
}
