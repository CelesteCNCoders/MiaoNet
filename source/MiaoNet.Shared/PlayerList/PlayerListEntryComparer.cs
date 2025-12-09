namespace MiaoNet.Shared;

public sealed class PlayerListEntryComparer : IComparer<IPlayerListEntry>
{
    public const string CelesteMapSet = "Celeste";

    public int Compare(IPlayerListEntry? x, IPlayerListEntry? y)
    {
        SafeGuard.Assert(x is not null);
        SafeGuard.Assert(y is not null);

        bool xIsEmpty = x.Location.IsEmpty;
        bool yIsEmpty = y.Location.IsEmpty;

        if (xIsEmpty && !yIsEmpty) return 1;
        if (!xIsEmpty && yIsEmpty) return -1;

        if (xIsEmpty && yIsEmpty)
            return string.Compare(x.PlayerInfo.Name, y.PlayerInfo.Name);

        bool xSp = x.Location.MapSet == CelesteMapSet;
        bool ySp = y.Location.MapSet == CelesteMapSet;

        if (xSp && !ySp) return -1;
        if (!xSp && ySp) return 1;

        int locationComparison = string.Compare(x.Location.MapSid, y.Location.MapSid);
        if (locationComparison != 0)
            return locationComparison;

        int sideComparison = x.Location.MapSide.CompareTo(y.Location.MapSide);
        if (sideComparison != 0)
            return sideComparison;

        int roomComparison = string.Compare(x.Location.MapRoom, y.Location.MapRoom);
        if (roomComparison != 0)
            return roomComparison;
        return string.Compare(x.PlayerInfo.Name, y.PlayerInfo.Name);
    }
}