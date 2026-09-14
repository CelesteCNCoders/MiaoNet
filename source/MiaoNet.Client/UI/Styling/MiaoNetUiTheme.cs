using Celeste.Mod.MiaoNet.UI.Geometry;

namespace Celeste.Mod.MiaoNet.UI.Styling;

// central color palette. entries keep their COLOR.* names so they line up with the
// parameter spec. nodes shouldn't hardcode colors; reference this table instead.
public static class MiaoNetUiTheme
{
    // chat message list and rows.
    public static class Chat
    {
        // COLOR.CHAT.BG: multiplied by fade and background opacity at draw time.
        public static readonly UiColor Background = UiColor.Black;

        // COLOR.CHAT.TIME.
        public static readonly UiColor Time = UiColor.CornflowerBlue;

        // COLOR.CHAT.TEXT: default for segments without an explicit color.
        public static readonly UiColor DefaultText = UiColor.White;
    }

    // chat channel tab bar.
    public static class Tab
    {
        // COLOR.TAB.BG, active.
        public static readonly UiColor ActiveBackground = UiColor.Black * 0.5f;

        // COLOR.TAB.BG, inactive.
        public static readonly UiColor IdleBackground = UiColor.Black * 0.15f;

        // COLOR.TAB.TEXT, active.
        public static readonly UiColor ActiveText = UiColor.White;

        // COLOR.TAB.TEXT, inactive.
        public static readonly UiColor IdleText = UiColor.White * 0.5f;
    }

    // chat input box.
    public static class Input
    {
        // COLOR.INPUT.BG: 0x7f / 255.
        public static readonly UiColor Background = UiColor.Black * (0x7f / 255f);

        // COLOR.INPUT.TEXT.
        public static readonly UiColor Text = UiColor.White;

        // COLOR.INPUT.IME.
        public static readonly UiColor ImeText = UiColor.Gray;

        // COLOR.INPUT.CARET.
        public static readonly UiColor Caret = UiColor.White;
    }

    // completion popup above the input box.
    public static class Completion
    {
        // COLOR.COMPL.BG: 0xaa / 255.
        public static readonly UiColor Background = UiColor.Black * (0xaa / 255f);

        // COLOR.COMPL.BORDER_TOP.
        public static readonly UiColor BorderTop = UiColor.Cyan;

        // COLOR.COMPL.BORDER_LEFT.
        public static readonly UiColor BorderLeft = UiColor.CornflowerBlue;

        // COLOR.COMPL.TEXT, unselected.
        public static readonly UiColor Text = UiColor.LightGray;

        // COLOR.COMPL.TEXT, selected.
        public static readonly UiColor SelectedText = UiColor.White;

        // COLOR.COMPL.SEL_BG: 0x22 / 255.
        public static readonly UiColor SelectedBackground = UiColor.Wheat * (0x22 / 255f);

        // COLOR.COMPL.SEL_BAR.
        public static readonly UiColor SelectedBar = UiColor.Wheat;
    }

    // debug-map overlay drawn over the level editor.
    public static class DebugMap
    {
        // player name colour; the marker square uses the player's own hair colour.
        public static readonly UiColor Name = UiColor.White;
    }

    // player list panel and rows.
    public static class PlayerList
    {
        // COLOR.PL.BG: 0xcc / 255.
        public static readonly UiColor Background = UiColor.Black * (0xcc / 255f);

        // COLOR.PL.BORDER_TOP, thickness 3.
        public static readonly UiColor BorderTop = UiColor.CornflowerBlue;

        // COLOR.PL.BORDER_LEFT, thickness 3.
        public static readonly UiColor BorderLeft = UiColor.Cyan;

        // COLOR.PL.HEADER.
        public static readonly UiColor Header = UiColor.Yellow;

        // COLOR.PL.STRIPE_EVEN: (0, 0, 0, 0x22).
        public static readonly UiColor StripeEven = UiColor.FromBytes(0x00, 0x00, 0x00, 0x22);

        // COLOR.PL.STRIPE_ODD: (0x22, 0x22, 0x22, 0x88).
        public static readonly UiColor StripeOdd = UiColor.FromBytes(0x22, 0x22, 0x22, 0x88);

        // COLOR.PL.PING.
        public static readonly UiColor Ping = UiColor.LightGray;

        // COLOR.PL.ROOM (room name and the separating colon).
        public static readonly UiColor Room = UiColor.LightGray;

        // COLOR.PL.ICON: tint for every status icon.
        public static readonly UiColor Icon = UiColor.White;

        // PL.ROW.DEFAULT_COLOR: unknown maps and sides.
        public static readonly UiColor UnknownMap = UiColor.LightGray;
    }
}
