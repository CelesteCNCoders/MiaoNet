namespace Celeste.Mod.MiaoNet;

public sealed class ClientState
{
    private readonly List<OnlinePlayer> players;

    public IReadOnlyList<OnlinePlayer> Players => players;

    public ClientState()
    {
        players = new();
        
    }
}