namespace Celeste.Mod.MiaoNet.UI.Input;

// a named UI action. the adapter turns physical input into these, the router decides which consumer
// (if any) gets each one.
public enum UiInputAction
{
    // default T.
    ChatToggle,

    // chat, pre-filled with the command prefix.
    ChatCommandToggle,

    Submit,

    // default Escape.
    Cancel,

    ChannelPrevious,

    ChannelNext,

    // edge only, history doesn't auto-repeat.
    HistoryUp,

    HistoryDown,

    // repeats while held.
    CompletionUp,

    CompletionDown,

    // repeats while held.
    CaretLeft,

    CaretRight,

    // default Tab.
    CompletionAccept,

    Paste,

    // default Tab.
    PlayerListToggle,

    PlayerListScrollUp,

    PlayerListScrollDown,

    // default PageUp.
    ChatListScrollUp,

    ChatListScrollDown,
}
