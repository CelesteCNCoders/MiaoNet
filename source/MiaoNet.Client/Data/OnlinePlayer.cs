using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class OnlinePlayer : IPlayerListEntry
{
    private PlayerLocation location;

    public int ID { get; }

    public OnlineChannel Channel { get; set; }

    public PlayerInfo Info { get; set; }

    public ref PlayerLocation Location => ref location;

    public PlayerState? State { get; set; }

    public PlayerGraphicsInfo? GraphicsInfo { get; set; }

    public PlayerGlobalFlags GlobalFlags { get; set; }

    public bool IsPaused => GlobalFlags.HasFlag(PlayerGlobalFlags.Paused);

    /// <summary><c>-1</c> No record.</summary>
    public int LastPing { get; set; }

    public OnlinePlayer(OnlineChannel channel, int id, PlayerInfo info, PlayerGlobalFlags globalFlags)
    {
        Channel = channel;
        ID = id;
        Info = info;
        location = PlayerLocation.Empty;
        GlobalFlags = globalFlags;
        LastPing = -1;
    }

    public override string ToString()
        => $"{Info} at {Location}";

    public string GetFullDisplayName() => string.IsNullOrEmpty(Info.Prefix)
        ? $":\0mn_avt_{ID}: {Info.Name}"
        : $":\0mn_avt_{ID}: [{Info.Prefix}] {Info.Name}";

    public string GetFullDisplayNameWithoutPrefix() =>
        $":\0mn_avt_{ID}: {Info.Name}";

    PlayerLocation IPlayerListEntry.Location => Location;

    PlayerInfo IPlayerListEntry.PlayerInfo => Info;
}