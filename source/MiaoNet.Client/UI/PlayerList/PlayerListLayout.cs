namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// player list layout constants
public static class PlayerListLayout
{
    // panel inset from the screen's left edge
    public const float PanelMarginX = 16f;

    // vertical margin before the first and after the last channel
    public const float PanelMarginY = 16f;

    public const float ChannelPaddingX = 16f;

    public const float ChannelPaddingY = 16f;

    public const float RowPaddingX = 4f;

    public const float RowPaddingY = 2f;

    // minimum gap between the name group and the location group
    public const float MiddlePadding = 32f;

    // gap on each side of the floating paused icon
    public const float PausedGap = 4f;

    public const float BorderThickness = 3f;

    public const float ChannelSpacing = 16f;

    // characters kept when a name is clipped
    public const int ClipLength = 24;

    // shorten an over-long map or room name for display
    public static string Clip(string value, ClipType clipType)
    {
        if (value.Length <= ClipLength)
        {
            return value;
        }

        return clipType switch
        {
            ClipType.KeepPrefix => $"{value[..ClipLength]}...",
            ClipType.KeepSuffix => $"...{value[^ClipLength..]}",
            _ => value,
        };
    }
}
