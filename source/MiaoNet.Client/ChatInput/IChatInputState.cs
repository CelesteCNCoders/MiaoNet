namespace Celeste.Mod.MiaoNet;

public interface IChatInputState
{
    public bool IsConnected { get; }

    public int Version { get; }

    public IReadOnlyCollection<string> AllPlayerNames { get; }

    public IReadOnlyCollection<string> PlayerNamesInScope(CommandSegmentType scope);

    public IReadOnlyCollection<string> PublicChannelNames { get; }
}
