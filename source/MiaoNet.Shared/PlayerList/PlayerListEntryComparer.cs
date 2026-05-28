namespace MiaoNet.Shared;

public sealed class PlayerListEntryComparer : IComparer<IPlayerListEntry>
{
    public const string CelesteMapSetPrefix = "Celeste/";

    public int Compare(IPlayerListEntry? x, IPlayerListEntry? y)
    {
        SafeGuard.Assert(x is not null);
        SafeGuard.Assert(y is not null);

        bool xIsEmpty = x.Location.IsEmpty;
        bool yIsEmpty = y.Location.IsEmpty;

        if (xIsEmpty && !yIsEmpty) return 1;
        if (!xIsEmpty && yIsEmpty) return -1;

        if (xIsEmpty && yIsEmpty)
            return string.Compare(x.PlayerInfo.Name, y.PlayerInfo.Name, StringComparison.Ordinal);

        bool xSp = x.Location.Map.Sid.StartsWith(CelesteMapSetPrefix, StringComparison.Ordinal);
        bool ySp = y.Location.Map.Sid.StartsWith(CelesteMapSetPrefix, StringComparison.Ordinal);

        if (xSp && !ySp) return -1;
        if (!xSp && ySp) return 1;

        if (x.IsLocallyKnownMap && !y.IsLocallyKnownMap)
            return -1;
        if (!x.IsLocallyKnownMap && y.IsLocallyKnownMap)
            return 1;

        int locationComparison = string.Compare(x.Location.Map.Sid, y.Location.Map.Sid, StringComparison.Ordinal);
        if (locationComparison != 0)
            return locationComparison;

        int sideComparison = x.Location.Map.AreaMode.CompareTo(y.Location.Map.AreaMode);
        if (sideComparison != 0)
            return sideComparison;

        int roomComparison = string.Compare(x.Location.Room, y.Location.Room, StringComparison.Ordinal);
        if (roomComparison != 0)
            return roomComparison;
        return string.Compare(x.PlayerInfo.Name, y.PlayerInfo.Name, StringComparison.Ordinal);
    }
}