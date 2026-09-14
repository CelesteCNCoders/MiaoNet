using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// one styled run of text inside a chat message.
public readonly record struct ChatTextRun(string Text, UiColor Color, TextDecoration Decorations);

// xna-free view model of one chat line. colors, decorations and the resolved timestamp all come
// from the app layer's adapter.
public sealed class ChatMessageRow
{
    // identity of the underlying message. the fade timers are keyed by this, so it has to stay
    // stable for as long as the message lives.
    public required object StableKey { get; init; }

    // null when the message has no timestamp (local or folded lines).
    public string? TimeText { get; init; }

    public required IReadOnlyList<ChatTextRun> Runs { get; init; }

    // how many messages folded into this line; 1 means no counter is drawn.
    public int RepeatCount { get; init; } = 1;
}

// supplies displayed chat rows to the virtual list.
public interface IChatMessageSource
{
    int Count { get; }

    object GetKey(int index);

    ChatMessageRow BuildRow(int index);
}
