namespace Celeste.Mod.MiaoNet.UI.Input;

// which UI currently owns the keyboard. set explicitly when a panel opens or closes.
//
// this replaces the old MiaoNetContext.HasComponentFocus flag, which doubled as the mutex between
// the chat and the player list. it was easy to leave set, and that permanently blocked the chat
// from opening. an explicit owner can't get stuck like that because it's derived from what's
// actually open.
public enum UiFocusOwner
{
    // nothing owns the keyboard: only open/close and chat list scrolling apply.
    None,

    // the chat input box is open.
    Chat,

    // the player list is open.
    PlayerList,
}
