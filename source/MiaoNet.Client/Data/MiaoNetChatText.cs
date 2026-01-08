using System.Globalization;
using System.Runtime.InteropServices;
using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    public static ChatText CreatePublicChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DimGray, FormatDateTime(dateTime)),
            new(ChatTextStyle.None, Color.Yellow, sender.Info.Name),
            new(ChatTextStyle.None, Color.White, ": "),
            ..ChatText.Parse(text, Color.White)
        ]);

    public static ChatText CreatePrivateChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DimGray, FormatDateTime(dateTime)),
            new(
                ChatTextStyle.None, Color.DarkGray,
                Dialog.Get("miaonet_chat_whisper_received")
                      .Replace(@"\[", "[") // idk how to escape '[]'s in Celeste dialogs
                      .Replace(@"\]", "]")
                      .Replace("(0)", sender.Info.Name)
            ),
            new(ChatTextStyle.None, Color.LightGray, ": "),
            ..ChatText.Parse(text, Color.LightGray)
        ]);

    public static ChatText CreateSentPrivateChat(DateTime dateTime, OnlinePlayer other, OnlinePlayer self, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DimGray, FormatDateTime(dateTime)),
            new(
                ChatTextStyle.None, Color.DarkGray,
                Dialog.Get("miaonet_chat_whisper_sent")
                      .Replace(@"\[", "[")
                      .Replace(@"\]", "]")
                      .Replace("(0)", other.Info.Name)
                      .Replace("(1)", self.Info.Name)
            ),
            new(ChatTextStyle.None, Color.LightGray, ": "),
            ..ChatText.Parse(text, Color.LightGray)
        ]);

    public static ChatText CreateAnnouncement(string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.Yellow, text)
        ]);

    public static ChatText CreateAnnouncement(DateTime dateTime, string text)
        => new ChatText([
            new(ChatTextStyle.None, Color.DimGray, FormatDateTime(dateTime)),
            new(ChatTextStyle.None, Color.Yellow, text)
        ]);

    public static ChatText CreateCommandTip(string text)
        => new ChatText([new(ChatTextStyle.None, Color.LightGray, text)]);

    public static ChatText CreateCommandEcho(string text)
        => new ChatText([new(ChatTextStyle.None, Color.DodgerBlue, text)]);

    public static ChatText CreateCommandErrorEcho(string text)
        => new ChatText([new(ChatTextStyle.None, Color.IndianRed, text)]);

    private static string FormatDateTime(DateTime dateTime)
        => $"[{dateTime.ToLocalTime():T}] ";
}
