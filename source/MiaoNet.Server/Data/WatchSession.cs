using MiaoNet.Shared;

namespace MiaoNet.Server;

public enum WatchSequenceResult
{
    Inactive,
    Next,
    Duplicate,
    Gap,
    ResyncPending,
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

    public void CompleteResync(int baselineSequence, uint playerEpoch = 0)
    {
        SafeGuard.Assert(IsActive && IsResyncPending && baselineSequence >= LastSequence);
        LastSequence = baselineSequence;
        PlayerEpoch = playerEpoch;
        lastWatcherResyncBaselineSequence = baselineSequence;
        IsResyncPending = false;
    }
}
