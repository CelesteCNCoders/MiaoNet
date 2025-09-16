using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext
{
    public delegate void PacketPlayerNotifyHandler(OnlinePlayer player);
    public delegate void PacketPlayerNotifyHandler<TPacket>(OnlinePlayer player, TPacket packet);

    public event Action<ClientState>? ClientInitialized;
    public event Action<OnlinePlayer>? PlayerJoined;
    public event Action<OnlinePlayer>? PlayerLeft;
    public event PacketPlayerNotifyHandler<PacketPlayerFrame>? PlayerFrameNotify;
    public event Action<OnlinePlayer, PacketPlayerMapChangedNotify>? PlayerMapChanging;
}