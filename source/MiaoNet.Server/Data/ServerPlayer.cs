using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerPlayer
{
    public ServerChannel Channel { get; }

    public PlayerInfo Info { get; set; }
    public PlayerLocationInfo LocationInfo { get; set; }
    public PlayerState? State { get; set; }
    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public int ID => Info.ID;

    public ServerPlayer(ServerChannel channel, PlayerInfo info, PlayerLocationInfo locationInfo)
    {
        Channel = channel;
        Info = info;
        LocationInfo = locationInfo;
    }

    public ChannelPlayerLocationInfo GetChannelPlayerLocationInfo()
        => new(Channel.ID, Info, LocationInfo);
}