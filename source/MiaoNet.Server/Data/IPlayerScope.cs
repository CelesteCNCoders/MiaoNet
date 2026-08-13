namespace MiaoNet.Server;

public interface IPlayerScope
{
    IEnumerable<MiaoClientConnection> Players { get; }
}