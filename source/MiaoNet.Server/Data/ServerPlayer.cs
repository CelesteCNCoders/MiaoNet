using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

[DebuggerDisplay("{Info} at {location}")]
public sealed class ServerPlayer
{
    private PlayerLocation location;

    public ServerChannel Channel { get; }

    public PlayerInfo Info { get; set; }

    public ref PlayerLocation Location => ref location;

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerOnlineStatus OnlineStatus { get; set; }

    public float LastPing { get; set; }

    public int ID => Info.ID;

    public ServerPlayer(ServerChannel channel, PlayerInfo info, PlayerLocation location)
    {
        Channel = channel;
        Info = info;
        Location = location;
    }
}