using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    private static readonly Color ColorCommand = Color.LightGray;
    private static readonly Color ColorCommandEcho = Color.DodgerBlue;
    private static readonly Color ColorCommandError = Color.IndianRed;
    private static readonly Color ColorAnnouncements = new Color(0x1b, 0xc2, 0xff);

    public static ChatText CreateAnnouncement(string text)
        => new ChatText([
            new(ColorAnnouncements, text)
        ]);

    public static ChatText CreateCommandTip(string text)
        => new ChatText([new(ColorCommand, text)]);

    public static ChatText CreateCommandEcho(string text)
        => new ChatText([new(ColorCommandEcho, text)]);

    public static ChatText CreateCommandError(string text)
        => new ChatText([new(ColorCommandError, text)]);
}
