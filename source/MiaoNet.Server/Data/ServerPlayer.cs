using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

[DebuggerDisplay("{Info} at {Location}")]
public sealed class ServerPlayer
{
    private readonly TokenBucket fireworksTokenBucket;

    public ServerChannel Channel { get; set; }

    public int ID { get; }

    public PlayerInfo Info { get; }

    public PlayerLocation Location { get; set; }

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerGlobalFlags GlobalFlags { get; set; }

    public ServerPlayer(ServerChannel channel, int id, PlayerInfo info)
    {
        fireworksTokenBucket = new(500, 500 * 3);

        Channel = channel;
        ID = id;
        Info = info;
        Location = PlayerLocation.Empty;
    }

    // no concurrent needed
    public bool TryConsumeFireworksToken()
        => fireworksTokenBucket.TryConsume();
}