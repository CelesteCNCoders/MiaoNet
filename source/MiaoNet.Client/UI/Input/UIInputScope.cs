namespace Celeste.Mod.MiaoNet.UI.Input;

// which ui is handling input. it is both the focus the router is in and the identity a panel
// registers its handle with, so one enum covers what used to be two.
//
// neutral is the state where nothing owns the keyboard. it can appear in a route's live list but is
// never an owner.
public enum UIInputScope
{
    Neutral,

    Chat,

    PlayerList,
}
