using System.Globalization;
using System.Runtime.InteropServices;
using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    private static readonly Color ColorTime = Color.DimGray;
    private static readonly Color ColorChat = Color.LightGray;
    private static readonly Color ColorCommand = Color.LightGray;
    private static readonly Color ColorCommandEcho = Color.DodgerBlue;
    private static readonly Color ColorCommandError = Color.IndianRed;
    private static readonly Color ColorPrivateChat = Color.LightGray;
    private static readonly Color ColorPrivateChatReceived = Color.DarkGray;
    private static readonly Color ColorMapChat = Color.Cyan;
    private static readonly Color ColorMapChatContent = Color.White;
    private static readonly Color ColorAnnouncements = Color.Yellow;

    public static ChatText CreatePublicChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(sender.Info.Color, sender.GetFullDisplayName()),
            new(ColorChat, ": "),
            ..ChatText.Parse(text, ColorChat)
        ]);

    public static ChatText CreateMapChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(ColorMapChat, Dialog.Clean("miaonet_chat_map_chat")),
            new(ColorChat, " "),
            new(sender.Info.Color, sender.GetFullDisplayName()),
            new(ColorMapChatContent, ": "),
            ..ChatText.Parse(text, ColorMapChatContent)
        ]);

    public static ChatText CreatePrivateChat(DateTime dateTime, OnlinePlayer sender, string text)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(
                ColorPrivateChatReceived,
                Dialog.Clean("miaonet_chat_whisper_received")
                      .Replace("(0)", sender.GetFullDisplayNameWithoutPrefix())
            ),
            new( ColorPrivateChat, ": "),
            ..ChatText.Parse(text, ColorPrivateChat)
        ]);

    public static ChatText CreateSentPrivateChat(DateTime dateTime, OnlinePlayer other, OnlinePlayer self, string text)
        => new ChatText([
            new( ColorTime, FormatDateTime(dateTime)),
            new(
                 ColorPrivateChatReceived,
                Dialog.Clean("miaonet_chat_whisper_sent")
                      .Replace("(0)", other.GetFullDisplayNameWithoutPrefix())
                      .Replace("(1)", self.GetFullDisplayNameWithoutPrefix())
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
