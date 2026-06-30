using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class OnlinePlayer
{
    public int ID { get; }

    public OnlineChannel Channel { get; set; }

    public PlayerInfo Info { get; set; }

    public PlayerLocation Location { get; set; }

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
        Location = PlayerLocation.Empty;
        GlobalFlags = globalFlags;
        LastPing = -1;
    }

    public string GetDisplayName(bool includePrefix, bool includeAvatarEmoji)
    {
        includePrefix &= !string.IsNullOrEmpty(Info.Prefix);

        return includeAvatarEmoji
            ? includePrefix 
                ? $":\0mn_avt_{ID}: [{Info.Prefix}] {Info.Name}" 
                : $":\0mn_avt_{ID}: {Info.Name}"
            : includePrefix 
                ? $"[{Info.Prefix}] {Info.Name}" 
                : Info.Name;
    }
}