using System.Globalization;
using System.Runtime.InteropServices;
using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    public static readonly Color ColorTime = Color.DimGray;
    public static readonly Color ColorChat = Color.White;
    public static readonly Color ColorCommand = Color.LightGray;
    public static readonly Color ColorCommandEcho = Color.DodgerBlue;
    public static readonly Color ColorCommandError = Color.IndianRed;
    public static readonly Color ColorPrivateChat = Color.LightGray;
    public static readonly Color ColorPrivateChatReceived = Color.DarkGray;
    public static readonly Color ColorAnnouncements = Color.Yellow;

    public static ChatText CreatePublicChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(sender.Info.Color, sender.Info.DisplayName),
            new(ColorChat, ": "),
            ..ChatText.Parse(text, ColorChat)
        ]);

    public static ChatText CreatePrivateChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(
                ColorPrivateChatReceived,
                Dialog.Get("miaonet_chat_whisper_received")
                      .Replace(@"\[", "[") // idk how to escape '[]'s in Celeste dialogs
                      .Replace(@"\]", "]")
                      .Replace("(0)", sender.Info.Name)
            ),
            new( ColorPrivateChat, ": "),
            ..ChatText.Parse(text, ColorPrivateChat)
        ]);

    public static ChatText CreateSentPrivateChat(DateTime dateTime, OnlinePlayer other, OnlinePlayer self, string text)
        => new ChatText([
            new( ColorTime, FormatDateTime(dateTime)),
            new(
                 ColorPrivateChatReceived,
                Dialog.Get("miaonet_chat_whisper_sent")
                      .Replace(@"\[", "[")
                      .Replace(@"\]", "]")
                      .Replace("(0)", other.Info.Name)
                      .Replace("(1)", self.Info.Name)
            ),
            new( ColorPrivateChat, ": "),
            ..ChatText.Parse(text, ColorPrivateChat)
        ]);

    public static ChatText CreateAnnouncement(string text)
        => new ChatText([
            new(ColorAnnouncements, text)
        ]);

    public static ChatText CreateAnnouncement(DateTime dateTime, string text)
        => new ChatText([
            new( ColorTime, FormatDateTime(dateTime)),
            new( ColorAnnouncements, text)
        ]);

    public static ChatText CreateCommandTip(string text)
        => new ChatText([new(ColorCommand, text)]);

    public static ChatText CreateCommandEcho(string text)
        => new ChatText([new(ColorCommandEcho, text)]);

    public static ChatText CreateCommandError(string text)
        => new ChatText([new(ColorCommandError, text)]);

    private static string FormatDateTime(DateTime dateTime)
        => $"[{dateTime.ToLocalTime():T}] ";
}
