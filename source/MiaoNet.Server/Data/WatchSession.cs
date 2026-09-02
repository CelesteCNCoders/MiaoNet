using MiaoNet.Shared;

namespace MiaoNet.Server;

public enum WatchSequenceResult
{
    Inactive,
    Next,
    Duplicate,
    Gap,
    ResyncPending,
    RestartSuspended,
}

public sealed class WatchSession
{
    private int lastWatcherResyncBaselineSequence = -1;
    private TimeSpan nextWatcherResyncAllowedAt;

    public int ID { get; }

    public int WatcherID { get; }

    public int TargetID { get; }

    public PlayerMapLocation Map { get; }

    public int StartRequestID { get; }

    public int LastSequence { get; private set; }

    public uint PlayerEpoch { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsResyncPending { get; private set; }

    public bool IsRestartSuspended { get; private set; }

    public WatchTargetRestartKind? RestartKind { get; private set; }

    public uint RestartEmptyLocationEpoch { get; private set; }

    public TimeSpan RestartExpiresAt { get; private set; }

    public WatchSession(int id, int watcherID, int targetID, PlayerMapLocation map, int startRequestID)
    {
        ID = id;
        WatcherID = watcherID;
        TargetID = targetID;
        Map = map;
        StartRequestID = startRequestID;
    }

    public void Activate(int baselineSequence, uint playerEpoch = 0)
    {
        SafeGuard.Assert(!IsActive);
        IsActive = true;
        LastSequence = baselineSequence;
        PlayerEpoch = playerEpoch;
    }

    public WatchSequenceResult AcceptSequence(
        int sequence,
        uint playerEpoch = 0,
        bool isEpochReplace = false
    )
    {
        if (!IsActive)
            return WatchSequenceResult.Inactive;
        if (IsRestartSuspended)
            return WatchSequenceResult.RestartSuspended;
        if (IsResyncPending)
            return WatchSequenceResult.ResyncPending;
        if (playerEpoch < PlayerEpoch)
            return WatchSequenceResult.Duplicate;
        if (playerEpoch > PlayerEpoch && !isEpochReplace)
        {
            IsResyncPending = true;
            return WatchSequenceResult.Gap;
        }
        if (sequence <= LastSequence)
            return WatchSequenceResult.Duplicate;
        if (sequence != LastSequence + 1)
        {
            IsResyncPending = true;
            return WatchSequenceResult.Gap;
        }

        LastSequence = sequence;
        PlayerEpoch = playerEpoch;
        return WatchSequenceResult.Next;
    }

    public bool TryBeginResync(
        int lastAppliedSequence,
        TimeSpan now,
        TimeSpan cooldown
    )
    {
        SafeGuard.Assert(cooldown >= TimeSpan.Zero);
        if (!IsActive
            || IsRestartSuspended
            || IsResyncPending
            || lastAppliedSequence < 0
            || lastAppliedSequence >= LastSequence
            || LastSequence <= lastWatcherResyncBaselineSequence
            || now < nextWatcherResyncAllowedAt)
            return false;

        IsResyncPending = true;
        lastWatcherResyncBaselineSequence = LastSequence;
        nextWatcherResyncAllowedAt = now + cooldown;
        return true;
    }

    public void SuspendForRestart(
        WatchTargetRestartKind kind,
        uint emptyLocationEpoch,
        TimeSpan expiresAt
    )
    {
        SafeGuard.Assert(IsActive);
        IsRestartSuspended = true;
        IsResyncPending = false;
        RestartKind = kind;
        RestartEmptyLocationEpoch = emptyLocationEpoch;
        RestartExpiresAt = expiresAt;
    }

    public bool CanContinueRestartAt(
        PlayerLocation location,
        uint playerEpoch,
        out bool beginResync
    )
    {
        beginResync = false;
        if (!IsActive || !IsRestartSuspended)
            return false;

        if (!location.IsInMap)
            return location == PlayerLocation.Empty
                && playerEpoch == RestartEmptyLocationEpoch;

        if (location.Map != Map
            || playerEpoch != PlayerTimelineSequence.Next(RestartEmptyLocationEpoch))
            return false;

        beginResync = true;
        return true;
    }

    public void BeginRestartResync()
    {
        SafeGuard.Assert(IsActive && IsRestartSuspended && !IsResyncPending);
        IsResyncPending = true;
    }

    public bool IsRestartExpired(TimeSpan now, uint emptyLocationEpoch)
        => IsActive
            && IsRestartSuspended
            && RestartEmptyLocationEpoch == emptyLocationEpoch
            && now >= RestartExpiresAt;

    public void CompleteResync(int baselineSequence, uint playerEpoch = 0)
    {
        SafeGuard.Assert(IsActive && IsResyncPending && baselineSequence >= LastSequence);
        LastSequence = baselineSequence;
        PlayerEpoch = playerEpoch;
        lastWatcherResyncBaselineSequence = baselineSequence;
        IsResyncPending = false;
        IsRestartSuspended = false;
        RestartKind = null;
        RestartEmptyLocationEpoch = 0;
        RestartExpiresAt = default;
    }
}
