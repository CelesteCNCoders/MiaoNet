using System.Collections.Immutable;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerPlayer
{
    public ServerChannel Channel { get; }

    public PlayerInfo Info { get; set; }
    public PlayerStateInfo StateInfo { get; set; }
    public PlayerStats? Stats { get; set; }
    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public int ID => Info.ID;

    public ServerPlayer(ServerChannel channel, PlayerInfo info, PlayerStateInfo stateInfo)
    {
        Channel = channel;
        Info = info;
        StateInfo = stateInfo;
    }

    public ChannelPlayerStateInfo GetChannelPlayerStateInfo()
        => new(Channel.ID, Info, StateInfo);
}