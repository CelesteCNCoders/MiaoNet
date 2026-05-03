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

        bool xSp = x.Location.MapSid.StartsWith(CelesteMapSetPrefix, StringComparison.Ordinal);
        bool ySp = y.Location.MapSid.StartsWith(CelesteMapSetPrefix, StringComparison.Ordinal);

        if (xSp && !ySp) return -1;
        if (!xSp && ySp) return 1;

        if (x.IsLocallyKnownMap && !y.IsLocallyKnownMap)
            return -1;
        if (!x.IsLocallyKnownMap && y.IsLocallyKnownMap)
            return 1;

        int locationComparison = string.Compare(x.Location.MapSid, y.Location.MapSid, StringComparison.Ordinal);
        if (locationComparison != 0)
            return locationComparison;

        int sideComparison = x.Location.Side.CompareTo(y.Location.Side);
        if (sideComparison != 0)
            return sideComparison;

        int roomComparison = string.Compare(x.Location.MapRoom, y.Location.MapRoom, StringComparison.Ordinal);
        if (roomComparison != 0)
            return roomComparison;
        return string.Compare(x.PlayerInfo.Name, y.PlayerInfo.Name, StringComparison.Ordinal);
    }
}