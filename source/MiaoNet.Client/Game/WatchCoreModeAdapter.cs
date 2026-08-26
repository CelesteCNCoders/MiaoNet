using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchCoreModeAdapter : IWatchEntityAdapter
{
    private static readonly WatchCoreModeAdapter instance = new();
    private static readonly WatchEntityKey StateKey = new(WatchEntityKind.CoreMode, 0);

    public WatchEntityKind Kind => WatchEntityKind.CoreMode;

    public static void Load()
        => WatchEntitySyncRegistry.Register(instance);

    public static void Unload()
        => WatchEntitySyncRegistry.Unregister(instance);

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        yield return new WatchEntityState(StateKey, [(byte)level.CoreMode]);
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        if (states.Count != 1)
            return WatchEntityApplyResult.None;

        WatchEntityState state = states.First();
        if (state.Key != StateKey
            || state.Payload.Length != 1
            || state.Payload.Span[0] > (byte)Session.CoreModes.Cold)
            return WatchEntityApplyResult.None;

        Session.CoreModes desired = (Session.CoreModes)state.Payload.Span[0];
        if (level.CoreMode == desired)
            return WatchEntityApplyResult.None;

        level.CoreMode = desired;
        level.Session.CoreMode = desired;
        return WatchEntityApplyResult.SceneChanged;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }
}
