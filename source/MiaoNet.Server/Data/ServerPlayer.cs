using System.Diagnostics;
using MiaoNet.Server.GameScope;
using MiaoNet.Shared;

namespace MiaoNet.Server;

[DebuggerDisplay("{Info} at {Location}")]
public sealed class ServerPlayer
{
    private readonly TokenBucket fireworksTokenBucket;
    
    public int ID { get; }
    
    public Scope? Scope { get; set; }
    
    public MiaoClientConnection? Connection { get; set; }

    public PlayerInfo Info { get; }

    public PlayerLocation Location { get; set; }

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerGlobalFlags GlobalFlags { get; set; }

    public ServerPlayer(int id, PlayerInfo info)
    {
        fireworksTokenBucket = new(500, 500 * 3);

        ID = id;
        Info = info;
        Location = PlayerLocation.Empty;
    }

    // no concurrent needed
    public bool TryConsumeFireworksToken()
        => fireworksTokenBucket.TryConsume();

    public override string ToString() => $"Player#{ID}({Info.Name})";
}