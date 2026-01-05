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

    public PlayerOnlineStatus OnlineStatus { get; set; }

    /// <summary><c>-1</c> No record.</summary>
    public int LastPing { get; set; }

    public OnlinePlayer(OnlineChannel channel, PlayerInfo info, PlayerOnlineStatus onlineStatus)
    {
        Channel = channel;
        Info = info;
        location = PlayerLocation.Empty;
        OnlineStatus = onlineStatus;
        LastPing = -1;
    }

    public override string ToString()
        => $"{Info} at {Location}";

    PlayerLocation IPlayerListEntry.Location => Location;

    PlayerInfo IPlayerListEntry.PlayerInfo => Info;
}