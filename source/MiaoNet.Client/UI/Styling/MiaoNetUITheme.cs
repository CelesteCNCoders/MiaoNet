using Celeste.Mod.MiaoNet.UI.Geometry;

namespace Celeste.Mod.MiaoNet.UI.Styling;

// central color palette. entries keep their COLOR.* names so they line up with the
// parameter spec. nodes shouldn't hardcode colors; reference this table instead.
public static class MiaoNetUITheme
{
    // chat message list and rows.
    public static class Chat
    {
        // COLOR.CHAT.BG: multiplied by fade and background opacity at draw time.
        public static readonly Color Background = Color.Black;

        // COLOR.CHAT.TIME.
        public static readonly Color Time = Color.CornflowerBlue;

        // COLOR.CHAT.TEXT: default for segments without an explicit color.
        public static readonly Color DefaultText = Color.White;
    }

    // chat channel tab bar.
    public static class Tab
    {
        // COLOR.TAB.BG, active.
        public static readonly Color ActiveBackground = Color.Black * 0.5f;

        // COLOR.TAB.BG, inactive.
        public static readonly Color IdleBackground = Color.Black * 0.15f;

        // COLOR.TAB.TEXT, active.
        public static readonly Color ActiveText = Color.White;

        // COLOR.TAB.TEXT, inactive.
        public static readonly Color IdleText = Color.White * 0.5f;
    }

    // chat input box.
    public static class Input
    {
        // COLOR.INPUT.BG: 0x7f / 255.
        public static readonly Color Background = Color.Black * (0x7f / 255f);

        // COLOR.INPUT.TEXT.
        public static readonly Color Text = Color.White;

        // COLOR.INPUT.IME.
        public static readonly Color ImeText = Color.Gray;

        // COLOR.INPUT.CARET.
        public static readonly Color Caret = Color.White;
    }

    // completion popup above the input box.
    public static class Completion
    {
        // COLOR.COMPL.BG: 0xaa / 255.
        public static readonly Color Background = Color.Black * (0xaa / 255f);

        // COLOR.COMPL.BORDER_TOP.
        public static readonly Color BorderTop = Color.Cyan;

        // COLOR.COMPL.BORDER_LEFT.
        public static readonly Color BorderLeft = Color.CornflowerBlue;

        // COLOR.COMPL.TEXT, unselected.
        public static readonly Color Text = Color.LightGray;

        // COLOR.COMPL.TEXT, selected.
        public static readonly Color SelectedText = Color.White;

        // COLOR.COMPL.SEL_BG: 0x22 / 255.
        public static readonly Color SelectedBackground = Color.Wheat * (0x22 / 255f);

        // COLOR.COMPL.SEL_BAR.
        public static readonly Color SelectedBar = Color.Wheat;
    }

    // debug-map overlay drawn over the level editor.
    public static class DebugMap
    {
        // player name colour; the marker square uses the player's own hair colour.
        public static readonly Color Name = Color.White;
    }

    // player list panel and rows.
    public static class PlayerList
    {
        // COLOR.PL.BG: 0xcc / 255.
        public static readonly Color Background = Color.Black * (0xcc / 255f);

        // COLOR.PL.BORDER_TOP, thickness 3.
        public static readonly Color BorderTop = Color.CornflowerBlue;

        // COLOR.PL.BORDER_LEFT, thickness 3.
        public static readonly Color BorderLeft = Color.Cyan;

        // COLOR.PL.HEADER.
        public static readonly Color Header = Color.Yellow;

        // COLOR.PL.STRIPE_EVEN: (0, 0, 0, 0x22).
        public static readonly Color StripeEven = new Color(0x00, 0x00, 0x00, 0x22);

        // COLOR.PL.STRIPE_ODD: (0x22, 0x22, 0x22, 0x88).
        public static readonly Color StripeOdd = new Color(0x22, 0x22, 0x22, 0x88);

        // COLOR.PL.PING.
        public static readonly Color Ping = Color.LightGray;

        // COLOR.PL.ROOM (room name and the separating colon).
        public static readonly Color Room = Color.LightGray;

        // COLOR.PL.ICON: tint for every status icon.
        public static readonly Color Icon = Color.White;

        // PL.ROW.DEFAULT_COLOR: unknown maps and sides.
        public static readonly Color UnknownMap = Color.LightGray;
    }
}
