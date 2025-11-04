using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class OnlinePlayer
{
    public int ID => Info.ID;

    public OnlineChannel Channel { get; set; }

    public PlayerInfo Info { get; set; }

    public PlayerLocationInfo LocationInfo { get; set; }

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public OnlinePlayer(
        OnlineChannel channel,
        PlayerInfo info,
        PlayerLocationInfo locationInfo,
        PlayerState? state, PlayerGraphicsInfo? graphicsInfo
    )
    {
        Channel = channel;
        Info = info;
        LocationInfo = locationInfo;
        State = state;
        GraphicsInfo = graphicsInfo;
    }

    public OnlinePlayer(
        OnlineChannel channel,
        PlayerInfo info,
        PlayerLocationInfo locationInfo
    ) : this(channel, info, locationInfo, null, null)
    {
    }

    public override string ToString()
        => $"{Info} at {LocationInfo}";
}