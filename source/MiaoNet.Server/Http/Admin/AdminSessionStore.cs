using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MiaoNet.Server;

public sealed class AdminSessionStore
{
    public sealed record AdminSession(int UserID, string UserName, string NickName, DateTimeOffset ExpiresAt);

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, DateTimeOffset> states = new();
    private readonly ConcurrentDictionary<string, AdminSession> sessions = new();
    private readonly TimeSpan sessionLifetime;

    public AdminSessionStore(TimeSpan sessionLifetime)
    {
        this.sessionLifetime = sessionLifetime;
    }

    public string CreateState()
    {
        Prune();
        string state = NewID();
        states[state] = DateTimeOffset.UtcNow + StateLifetime;
        return state;
    }

    public bool ConsumeState(string state)
    {
        if (!states.TryRemove(state, out DateTimeOffset expiresAt))
            return false;
        return DateTimeOffset.UtcNow <= expiresAt;
    }

    public string CreateSession(int userID, string userName, string nickName)
    {
        Prune();
        string id = NewID();
        sessions[id] = new AdminSession(userID, userName, nickName, DateTimeOffset.UtcNow + sessionLifetime);
        return id;
    }

    public AdminSession? GetSession(string id)
    {
        if (!sessions.TryGetValue(id, out AdminSession? session))
            return null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now > session.ExpiresAt)
        {
            sessions.TryRemove(id, out _);
            return null;
        }
        // sliding renewal
        AdminSession renewed = session with { ExpiresAt = now + sessionLifetime };
        sessions.TryUpdate(id, renewed, session);
        return renewed;
    }

    public bool DeleteSession(string id) => sessions.TryRemove(id, out _);

    private void Prune()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var p in states)
        {
            if (now > p.Value)
                states.TryRemove(p.Key, out _);
        }
        foreach (var p in sessions)
        {
            if (now > p.Value.ExpiresAt)
                sessions.TryRemove(p.Key, out _);
        }
    }

    private static string NewID() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
