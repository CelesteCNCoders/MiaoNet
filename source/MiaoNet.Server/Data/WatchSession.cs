using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class WatchSession
{
    public int ID { get; }

    public int WatcherID { get; }

    public int TargetID { get; }

    public PlayerMapLocation Map { get; }

    public int StartRequestID { get; }

    public int LastSequence { get; private set; }

    public bool IsActive { get; private set; }

    public WatchSession(int id, int watcherID, int targetID, PlayerMapLocation map, int startRequestID)
    {
        ID = id;
        WatcherID = watcherID;
        TargetID = targetID;
        Map = map;
        StartRequestID = startRequestID;
    }

    public void Activate(int baselineSequence)
    {
        SafeGuard.Assert(!IsActive);
        IsActive = true;
        LastSequence = baselineSequence;
    }

    public bool TryAdvanceSequence(int sequence)
    {
        if (!IsActive || sequence != LastSequence + 1)
            return false;

        LastSequence = sequence;
        return true;
    }
}
