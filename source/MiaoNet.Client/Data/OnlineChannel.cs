using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class OnlineChannel
{
    public int ID { get; set; }

    public ChannelInfo Info { get; }

    public Dictionary<int, OnlinePlayer> Players { get; set; }

    public OnlineChannel(int id, ChannelInfo channelInfo)
    {
        ID = id;
        Info = channelInfo;
        Players = new();
    }
}
