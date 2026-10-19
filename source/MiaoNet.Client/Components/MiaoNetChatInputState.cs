using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetChatInputState : IChatInputState
{
    private readonly MiaoNetContext context;

    private int snapshotVersion = -1;
    private IReadOnlyCollection<string> allPlayerNames = [];
    private IReadOnlyCollection<string> publicChannelNames = [];
    private readonly Dictionary<CommandSegmentType, IReadOnlyCollection<string>> playerNamesInScope = [];

    public MiaoNetChatInputState(MiaoNetContext context)
    {
        this.context = context;
    }

    private ClientState? State => context.ClientState;

    public bool IsConnected => State is not null;

    public int Version => State?.Version ?? 0;

    public IReadOnlyCollection<string> AllPlayerNames
    {
        get
        {
            Refresh();
            return allPlayerNames;
        }
    }

    public IReadOnlyCollection<string> PublicChannelNames
    {
        get
        {
            Refresh();
            return publicChannelNames;
        }
    }

    public IReadOnlyCollection<string> PlayerNamesInScope(CommandSegmentType scope)
    {
        Refresh();
        if (playerNamesInScope.TryGetValue(scope, out IReadOnlyCollection<string>? cached))
            return cached;

        IReadOnlyCollection<string> names = State is { } state
            ? CommandSegmentResolver.ResolvePlayers(state, scope).Select(p => p.Info.Name).ToArray()
            : [];
        playerNamesInScope[scope] = names;
        return names;
    }

    private void Refresh()
    {
        int version = Version;
        if (version == snapshotVersion)
            return;

        snapshotVersion = version;
        playerNamesInScope.Clear();

        if (State is { } state)
        {
            allPlayerNames = state.AllPlayers.Select(p => p.Info.Name).ToArray();
            publicChannelNames = CommandSegmentResolver.ResolveChannels(state).Select(c => c.Info.Name).ToArray();
        }
        else
        {
            allPlayerNames = [];
            publicChannelNames = [];
        }
    }
}
