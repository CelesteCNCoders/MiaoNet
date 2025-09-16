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

    public OnlinePlayer(OnlineChannel channel, PlayerInfo info, PlayerLocationInfo locationInfo)
    {
        Channel = channel;
        Info = info;
        LocationInfo = locationInfo;
        Channel = channel;
    }

    public override string ToString()
        => $"{Info} at {LocationInfo}";
}