using System.Collections.Immutable;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Chat;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet;

// adapts the chat component into the XNA-free UI view model.
// lives next to the component so it can read the log model without widening accessibility.
public sealed partial class ChatComponent
{
    // bumps whenever something the chat UI shows changes. manager's mutation counter plus the
    // open state, which also selects which log we display.
    internal int UIVersion => (chatManager.Version * 2) + (Active ? 1 : 0);

    internal ChatUISnapshot BuildUISnapshot()
    {
        ChatMessageManager manager = chatManager;

        List<ChatItem> full = manager.ChatLog;
        var keys = new object[full.Count];
        var repeats = new int[full.Count];
        for (int i = 0; i < full.Count; i++)
        {
            keys[i] = full[i];
            repeats[i] = full[i].RepeatCount;
        }

        return new ChatUISnapshot
        {
            FullLogKeys = keys,
            RepeatCounts = repeats,
            Display = new DisplaySource(SelectDisplayedLog(manager)),
            TabNames = manager.TabNameList,
            ActiveTabIndex = manager.ActiveTabIndex,
            InitialTitle = initialTabTitle,
        };
    }

    // which log to show: active while open/WithTab, full for ShowAll, else nothing
    private List<ChatItem> SelectDisplayedLog(ChatMessageManager manager)
    {
        NewMessageShowingMode mode = MiaoNetModule.Settings.NewMessagesShowing;

        if (Active || mode == NewMessageShowingMode.WithTab)
        {
            return manager.ActiveChatLog;
        }

        if (mode == NewMessageShowingMode.ShowAll)
        {
            return manager.ChatLog;
        }

        return [];
    }

    private static ChatMessageRow ToRow(ChatItem item)
    {
        ImmutableArray<ChatTextSegment> segments = item.MessageText.Segments;
        var runs = new List<ChatTextRun>(segments.Length);
        foreach (ChatTextSegment segment in segments)
        {
            runs.Add(new ChatTextRun(segment.Text, ToUIColor(segment.Color), ToDecoration(segment.Style)));
        }

        return new ChatMessageRow
        {
            // the ChatItem itself is the stable key
            StableKey = item,
            TimeText = item.DateTimeText,
            Runs = runs,
            RepeatCount = item.RepeatCount,
        };
    }

    private static TextDecoration ToDecoration(ChatTextStyle style)
    {
        TextDecoration decorations = TextDecoration.None;
        if (style.HasFlag(ChatTextStyle.Outline))
        {
            decorations |= TextDecoration.Outline;
        }
        if (style.HasFlag(ChatTextStyle.Underscore))
        {
            decorations |= TextDecoration.Underline;
        }
        if (style.HasFlag(ChatTextStyle.Strikethrough))
        {
            decorations |= TextDecoration.Strikethrough;
        }
        return decorations;
    }

    private static UIColor ToUIColor(Color color)
        => UIColor.FromBytes(color.R, color.G, color.B, color.A);

    private sealed class DisplaySource(List<ChatItem> items) : IChatMessageSource
    {
        public int Count => items.Count;

        public object GetKey(int index) => items[index];

        public ChatMessageRow BuildRow(int index) => ToRow(items[index]);
    }
}

// snapshot of everything the chat UI needs; rebuilt only when the log changes
internal sealed class ChatUISnapshot
{
    public required object[] FullLogKeys { get; init; }

    public required int[] RepeatCounts { get; init; }

    public required IChatMessageSource Display { get; init; }

    public required IReadOnlyList<string> TabNames { get; init; }

    public required int ActiveTabIndex { get; init; }

    public required string InitialTitle { get; init; }
}
