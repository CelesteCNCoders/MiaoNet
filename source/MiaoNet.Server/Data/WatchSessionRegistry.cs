using MiaoNet.Shared;
using System.Collections.ObjectModel;

namespace MiaoNet.Server;

public sealed class WatchSessionRegistry
{
    private sealed class TargetSessions
    {
        public HashSet<WatchSession> Mutable { get; } = [];

        public ReadOnlySet<WatchSession> View { get; }

        public TargetSessions()
            => View = new(Mutable);
    }

    private int nextSessionID;

    private readonly Dictionary<int, WatchSession> sessions;
    private readonly Dictionary<int, int> sessionsByWatcher;
    private readonly Dictionary<int, TargetSessions> sessionsByTarget;

    public int Count => sessions.Count;

    public WatchSessionRegistry()
    {
        sessions = new();
        sessionsByWatcher = new();
        sessionsByTarget = new();
    }

    public WatchSession Add(
        int watcherID,
        int targetID,
        PlayerMapLocation map,
        int startRequestID
    )
    {
        SafeGuard.Assert(!sessionsByWatcher.ContainsKey(watcherID));

        int sessionID = ++nextSessionID;
        WatchSession session = new(sessionID, watcherID, targetID, map, startRequestID);
        sessions.Add(sessionID, session);
        sessionsByWatcher.Add(watcherID, sessionID);

        if (!sessionsByTarget.TryGetValue(targetID, out TargetSessions? targetSessions))
        {
            targetSessions = new();
            sessionsByTarget.Add(targetID, targetSessions);
        }
        targetSessions.Mutable.Add(session);

        return session;
    }

    public bool TryGet(int sessionID, out WatchSession? session)
        => sessions.TryGetValue(sessionID, out session);

    public bool TryGetByWatcher(int watcherID, out WatchSession? session)
    {
        if (sessionsByWatcher.TryGetValue(watcherID, out int sessionID))
            return sessions.TryGetValue(sessionID, out session);

        session = null;
        return false;
    }

    public IReadOnlyCollection<WatchSession> GetByTarget(int targetID)
    {
        if (!sessionsByTarget.TryGetValue(targetID, out TargetSessions? targetSessions))
            return [];

        return targetSessions.View;
    }

    public bool HasWatcher(int watcherID)
        => sessionsByWatcher.ContainsKey(watcherID);

    public bool HasTarget(int targetID)
        => sessionsByTarget.ContainsKey(targetID);

    public bool Remove(int sessionID, out WatchSession? session)
    {
        if (!sessions.Remove(sessionID, out session))
            return false;

        bool watcherRemoved = sessionsByWatcher.Remove(session.WatcherID);
        SafeGuard.Assert(watcherRemoved);

        TargetSessions targetSessions = sessionsByTarget[session.TargetID];
        bool targetRemoved = targetSessions.Mutable.Remove(session);
        SafeGuard.Assert(targetRemoved);
        if (targetSessions.Mutable.Count == 0)
            sessionsByTarget.Remove(session.TargetID);

        return true;
    }

    public IReadOnlyCollection<WatchSession> RemoveAllForPlayer(int playerID)
    {
        HashSet<int> sessionIDs = new();
        if (sessionsByWatcher.TryGetValue(playerID, out int watchedSessionID))
            sessionIDs.Add(watchedSessionID);
        if (sessionsByTarget.TryGetValue(playerID, out TargetSessions? producedSessions))
            foreach (WatchSession session in producedSessions.View)
                sessionIDs.Add(session.ID);

        WatchSession[] removed = new WatchSession[sessionIDs.Count];
        int index = 0;
        foreach (int sessionID in sessionIDs)
        {
            bool result = Remove(sessionID, out WatchSession? session);
            SafeGuard.Assert(result);
            removed[index++] = session!;
        }
        return removed;
    }
}
