namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRemoteStrawberryAppearances
{
    private readonly HashSet<int> ghostIDs = [];
    private string room = string.Empty;

    internal bool HasState { get; private set; }

    internal void Apply(string room, IEnumerable<int> ids)
    {
        this.room = room;
        ghostIDs.Clear();
        ghostIDs.UnionWith(ids);
        HasState = true;
    }

    internal bool TryGet(string room, int id, out bool isGhost)
    {
        isGhost = HasState
            && StringComparer.Ordinal.Equals(this.room, room)
            && ghostIDs.Contains(id);
        return HasState && StringComparer.Ordinal.Equals(this.room, room);
    }
}
