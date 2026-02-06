using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

[DebuggerDisplay("{Info} at {location}")]
public sealed class ServerPlayer
{
    private readonly TokenBucket fireworksTokenBucket;

    private PlayerLocation location;

    public ServerChannel Channel { get; }

    public PlayerInfo Info { get; set; }

    public ref PlayerLocation Location => ref location;

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerGlobalFlags GlobalFlags { get; set; }

    public int ID => Info.ID;

    public ServerPlayer(ServerChannel channel, PlayerInfo info)
    {
        fireworksTokenBucket = new(500, 500 * 3);

        Channel = channel;
        Info = info;
        Location = PlayerLocation.Empty;
    }

    // no concurrent needed
    public bool TryConsumeFireworksToken()
        => fireworksTokenBucket.TryConsume();
}