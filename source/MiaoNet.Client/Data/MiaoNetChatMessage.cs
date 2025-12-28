using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    public static ChatText CreatePublicChat(OnlinePlayer sender, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.Yellow, sender.Info.Name),
            new(ChatTextStyle.None, Color.White, ": "),
            new(ChatTextStyle.None, Color.White, text)
        ]);

    public static ChatText CreatePrivateChat(OnlinePlayer sender, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DarkGray, $"[whisper] {sender.Info.Name}"),
            new(ChatTextStyle.None, Color.LightGray, ": "),
            new(ChatTextStyle.None, Color.LightGray, text)
        ]);

    public static ChatText CreateSentPrivateChat(OnlinePlayer other, OnlinePlayer self, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DarkGray, $"[whisper to {other.Info.Name}] {self.Info.Name}"),
            new(ChatTextStyle.None, Color.LightGray, ": "),
            new(ChatTextStyle.None, Color.LightGray, text)
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
