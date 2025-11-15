using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext
{
    public delegate void PacketPlayerNotificationHandler(OnlinePlayer player);
    public delegate void PacketPlayerNotificationHandler<TPacket>(OnlinePlayer player, TPacket packet);

    public event Action<ClientState>? ClientInitialized;
    public event Action<OnlinePlayer>? PlayerJoined;
    public event Action<OnlinePlayer>? PlayerLeft;
    public event PacketPlayerNotificationHandler<PacketPlayerFrame>? PlayerFrameNotification;
    public event PacketPlayerNotificationHandler<PacketPlayerMapChangedNotification>? PlayerMapChanged;
    public event Action<OnlinePlayer, string>? PlayerMapRoomChanged;
    public event Action<PacketPlayerMapChangedResponse>? PlayerMapChangeResponse;
}