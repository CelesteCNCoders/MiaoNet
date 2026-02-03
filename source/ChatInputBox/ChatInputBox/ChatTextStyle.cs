namespace Celeste.Mod.ChatInputBox;

[Flags]
public enum ChatTextStyle
{
    None = 0,
    Underscore = 1 << 0,
    Strikethrough = 1 << 1,
    Outline = 1 << 2
}