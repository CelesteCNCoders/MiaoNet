namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// how an over-long map or room name gets shortened for display.
// lives with the rest of the player list presentation policy so the rule can be unit tested.
public enum ClipType
{
    // show the whole string
    None,

    // keep the first PlayerListLayout.ClipLength characters
    KeepPrefix,

    // keep the last PlayerListLayout.ClipLength characters
    KeepSuffix,
}
