using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class OnlinePlayer : IPlayerListEntry
{
    private PlayerLocation location;

    public int ID => Info.ID;

    public OnlineChannel Channel { get; set; }

    public PlayerInfo Info { get; set; }

    public ref PlayerLocation Location => ref location;

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public OnlinePlayer(
        OnlineChannel channel,
        PlayerInfo info,
        PlayerLocation locationInfo,
        PlayerState? state, PlayerGraphicsInfo? graphicsInfo
    )
    {
        Channel = channel;
        Info = info;
        Location = locationInfo;
        State = state;
        GraphicsInfo = graphicsInfo;
    }

    public OnlinePlayer(
        OnlineChannel channel,
        PlayerInfo info,
        PlayerLocation locationInfo
    ) : this(channel, info, locationInfo, null, null)
    {
    }

    public override string ToString()
        => $"{Info} at {Location}";


    PlayerLocation IPlayerListEntry.Location => Location;

    PlayerInfo IPlayerListEntry.PlayerInfo => Info;
}