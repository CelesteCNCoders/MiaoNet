using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;
using MiaoNet.Server.GameScope;

namespace MiaoNet.Server;

[DebuggerDisplay("Channel #{ID} {Info.Name}")]
public sealed class ServerChannel
{
    public int ID { get; }

    public ChannelScope? Scope { get; set; }

    public ChannelInfo Info { get; }

    public ServerChannel(int id, ChannelInfo info)
    {
        ID = id;
        Info = info;
    }

    public override string ToString() => $"Channel#{ID}({Info.Name})";
}