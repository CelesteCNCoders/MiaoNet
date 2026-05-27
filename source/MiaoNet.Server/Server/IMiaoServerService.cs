using MiaoNet.Shared;

namespace MiaoNet.Server;

public interface IMiaoServerService
{
    public IReadOnlyDictionary<int, MiaoClientConnection> Players { get; }

    public IReadOnlyDictionary<int, ServerChannel> Channels { get; }

    public Task BroadcastAsync(IContextlessPacket packet);
}
