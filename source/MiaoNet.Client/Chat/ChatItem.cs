using System;
using System.Globalization;

namespace Celeste.Mod.MiaoNet.Chat;

// one line in a chat log: formatted timestamp, parsed content and fold count.
// rendering lives in MiaoNet's UI layer now, so this is pure data (no renderer, no Draw).
public class ChatItem
{
    private readonly string? dateTimeText;
    private readonly ChatText messageText;

    // how many times this message has been folded into, 1 means no counter drawn
    public int RepeatCount { get; set; } = 1;

    public ChatItem(DateTime dateTime, ChatText messageText)
    {
        dateTimeText = FormatDateTime(dateTime);
        this.messageText = messageText;
    }

    public ChatItem(ChatText messageText)
    {
        this.messageText = messageText;
    }

    // formatted timestamp, null when the message has none
    public string? DateTimeText => dateTimeText;

    // parsed content with per-segment colors and styles
    public ChatText MessageText => messageText;

    private static string FormatDateTime(DateTime dateTime)
        => dateTime.ToLocalTime().ToString("T", CultureInfo.InvariantCulture);
}
