using Microsoft.Extensions.Logging;
using MiaoNet.Shared;

namespace MiaoNet.Server.GameScope;

public sealed class ChannelScope: Scope<int, PlayerMap>
{
    private readonly ILogger logger;
    private readonly object ensureLock = new();

    public ServerChannel Channel { get; }

    public override bool Permanent => false;

    public ChannelScope(ServerChannel channel, GlobalScope parent, ILogger logger): base(channel.ID, parent)
    {
        this.logger = logger;
        this.Channel = channel;
        channel.Scope = this;
        parent.AddChild(channel.ID, this);
    }

    public MapScope EnsureMapScope(PlayerMap map)
    {
        lock (ensureLock)
        {
            foreach (var child in Children.Values)
            {
                if (child is MapScope existing && existing.Map == map)
                    return existing;
            }

            return new MapScope(map, this, logger);
        }
    }

    public override string ToString() => $"Channel#{Channel.ID}({Channel.Info.Name})";
}
