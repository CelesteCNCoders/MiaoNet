using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    public static ChatText CreatePublicChat(OnlinePlayer sender, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.Yellow, sender.Info.Name),
            new(ChatTextStyle.None, Color.White, ": "),
            ..ChatText.Parse(text, Color.White)
        ]);

    public static ChatText CreatePrivateChat(OnlinePlayer sender, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DarkGray, $"[whisper] {sender.Info.Name}"),
            new(ChatTextStyle.None, Color.LightGray, ": "),
            ..ChatText.Parse(text, Color.LightGray)
        ]);

    public static ChatText CreateSentPrivateChat(OnlinePlayer other, OnlinePlayer self, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DarkGray, $"[whisper to {other.Info.Name}] {self.Info.Name}"),
            new(ChatTextStyle.None, Color.LightGray, ": "),
            ..ChatText.Parse(text, Color.LightGray)
        ]);

    public static ChatText CreateAnnouncement(string text)
        => new ChatText([new(ChatTextStyle.None, Color.Yellow, text)]);

    public static ChatText CreateCommandTip(string text)
        => new ChatText([new(ChatTextStyle.None, Color.LightGray, text)]);

    public static ChatText CreateCommandEcho(string text)
        => new ChatText([new(ChatTextStyle.None, Color.DodgerBlue, text)]);

    public static ChatText CreateCommandErrorEcho(string text)
        => new ChatText([new(ChatTextStyle.None, Color.IndianRed, text)]);
}
