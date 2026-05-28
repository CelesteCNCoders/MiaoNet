using System.Globalization;
using System.Runtime.InteropServices;
using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetChatText
{
    private static readonly Color ColorTime = Color.DimGray;
    private static readonly Color ColorChatContent = Color.White;
    private static readonly Color ColorMapChat = Color.Cyan;
    private static readonly Color ColorChannelChat = Color.LightGray;
    private static readonly Color ColorPrivateChat = Color.LightGray;
    private static readonly Color ColorPrivateChatReceived = Color.DarkGray;
    private static readonly Color ColorCommand = Color.LightGray;
    private static readonly Color ColorCommandEcho = Color.DodgerBlue;
    private static readonly Color ColorCommandError = Color.IndianRed;
    private static readonly Color ColorAnnouncements = Color.Yellow;

    public static ChatText CreatePublicChat(DateTime dateTime, OnlinePlayer sender, string text, bool avatar)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(sender.Info.Color, sender.GetDisplayName(true, avatar)),
            new(ColorChatContent, ": "),
            ..ChatText.Parse(text, ColorChatContent)
        ]);

    public static ChatText CreateMapChat(DateTime dateTime, OnlinePlayer sender, string text, bool avatar)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(ColorMapChat, Dialog.Clean("miaonet_chat_map_chat")),
            new(ColorChatContent, " "),
            new(sender.Info.Color, sender.GetDisplayName(true, avatar)),
            new(ColorChatContent, ": "),
            ..ChatText.Parse(text, ColorChatContent)
        ]);

    public static ChatText CreateChannelChat(DateTime dateTime, OnlinePlayer sender, string text, bool avatar)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(ColorChannelChat, Dialog.Clean("miaonet_chat_channel_chat")),
            new(ColorChatContent, " "),
            new(sender.Info.Color, sender.GetDisplayName(true, avatar)),
            new(ColorChatContent, ": "),
            ..ChatText.Parse(text, ColorChatContent)
        ]);

    public static ChatText CreatePrivateChat(DateTime dateTime, OnlinePlayer sender, string text, bool avatar)
        => new ChatText([
            new(ColorTime, FormatDateTime(dateTime)),
            new(
                ColorPrivateChatReceived,
                PFormat.Format(
                    CultureInfo.CurrentCulture,
                    Dialog.Clean("miaonet_chat_whisper_received"),
                    sender.GetDisplayName(false, avatar)
                )
            ),
            new( ColorPrivateChat, ": "),
            ..ChatText.Parse(text, ColorPrivateChat)
        ]);

    public static ChatText CreateSentPrivateChat(DateTime dateTime, OnlinePlayer other, OnlinePlayer self, string text, bool avatar)
        => new ChatText([
            new( ColorTime, FormatDateTime(dateTime)),
            new(
                 ColorPrivateChatReceived,
                PFormat.Format(
                    CultureInfo.CurrentCulture,
                    Dialog.Clean("miaonet_chat_whisper_sent"),
                    other.GetDisplayName(false, avatar),
                    self.GetDisplayName(false, avatar)
                )
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
