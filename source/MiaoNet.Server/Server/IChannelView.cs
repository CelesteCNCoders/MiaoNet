using MiaoNet.Shared;

namespace MiaoNet.Server;

public interface IChannelView
{
    int ID { get; }

    ChannelInfo Info { get; }

    IEnumerable<MiaoClientConnection> Players { get; }
}
