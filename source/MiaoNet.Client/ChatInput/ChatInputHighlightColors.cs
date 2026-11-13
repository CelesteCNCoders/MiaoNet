using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class ChatInputHighlightColors
{
    public static readonly Color Text = new(255, 255, 255);
    public static readonly Color Command = new(85, 255, 85);
    public static readonly Color Player = new(255, 215, 0);
    public static readonly Color Channel = new(85, 215, 255);
    public static readonly Color NewChannel = new(185, 135, 255);
    public static readonly Color PrivateChannel = new(85, 135, 185);
    public static readonly Color ChatChannelType = new(85, 215, 255);
    public static readonly Color Emoji = new(255, 135, 85);
    public static readonly Color Invalid = new(255, 85, 85);
}
