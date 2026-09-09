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

    private int lastPing = -1;

    /// <summary>
    /// -1 if not measured
    /// Currently written by the heartbeat task, read by the admin panel
    /// </summary>
    public int LastPing
    {
        get => Volatile.Read(ref lastPing);
        set => Volatile.Write(ref lastPing, value);
    }

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