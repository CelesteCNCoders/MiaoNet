namespace Celeste.Mod.MiaoNet.UI.Input;

// which module owns an action. not the same thing as UiFocusOwner: the chat owns the chat-list
// paging keys even when nothing has the keyboard at all.
public enum UIInputConsumer
{
    // the chat: input box, tab strip and message list.
    Chat,

    // the player list panel.
    PlayerList,
}
