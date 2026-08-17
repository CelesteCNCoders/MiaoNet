using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class OnlineChannel
{
    public int ID { get; }

    public ChannelInfo Info { get; }

    public bool IsPrivate => Info.IsPrivate;

    public HashSet<OnlinePlayer> Players { get; }

    public OnlineChannel(int id, ChannelInfo channelInfo)
    {
        ID = id;
        Info = channelInfo;
        Players = new();
    }
}
