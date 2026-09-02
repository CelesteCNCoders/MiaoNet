namespace Celeste.Mod.MiaoNet;

internal sealed class WatchEntityCaptureCursor
{
    private int nextIndex;

    internal int GetStartIndex(int itemCount)
    {
        if (itemCount <= 0)
            return 0;
        nextIndex %= itemCount;
        return nextIndex;
    }

    internal void Advance(int processedCount, int itemCount)
    {
        if (itemCount <= 0)
        {
            nextIndex = 0;
            return;
        }
        nextIndex = (GetStartIndex(itemCount) + processedCount) % itemCount;
    }

    internal void Reset() => nextIndex = 0;
}
