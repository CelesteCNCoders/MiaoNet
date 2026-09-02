namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRemoteCompletionGate
{
    // A batched scene update can deliver Gone before the local replay has caught up.
    internal bool AuthoritativeGone { get; private set; }
    internal bool VisualComplete { get; private set; }

    internal void Reset()
    {
        AuthoritativeGone = false;
        VisualComplete = false;
    }

    internal bool MarkAuthoritativeGone()
    {
        AuthoritativeGone = true;
        return VisualComplete;
    }

    internal bool MarkVisualComplete()
    {
        VisualComplete = true;
        return AuthoritativeGone;
    }
}
