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
        public static readonly UIColor Background = UIColor.Black;

        // COLOR.CHAT.TIME.
        public static readonly UIColor Time = UIColor.CornflowerBlue;

        // COLOR.CHAT.TEXT: default for segments without an explicit color.
        public static readonly UIColor DefaultText = UIColor.White;
    }

    // chat channel tab bar.
    public static class Tab
    {
        // COLOR.TAB.BG, active.
        public static readonly UIColor ActiveBackground = UIColor.Black * 0.5f;

        // COLOR.TAB.BG, inactive.
        public static readonly UIColor IdleBackground = UIColor.Black * 0.15f;

        // COLOR.TAB.TEXT, active.
        public static readonly UIColor ActiveText = UIColor.White;

        // COLOR.TAB.TEXT, inactive.
        public static readonly UIColor IdleText = UIColor.White * 0.5f;
    }

    // chat input box.
    public static class Input
    {
        // COLOR.INPUT.BG: 0x7f / 255.
        public static readonly UIColor Background = UIColor.Black * (0x7f / 255f);

        // COLOR.INPUT.TEXT.
        public static readonly UIColor Text = UIColor.White;

        // COLOR.INPUT.IME.
        public static readonly UIColor ImeText = UIColor.Gray;

        // COLOR.INPUT.CARET.
        public static readonly UIColor Caret = UIColor.White;
    }

    // completion popup above the input box.
    public static class Completion
    {
        // COLOR.COMPL.BG: 0xaa / 255.
        public static readonly UIColor Background = UIColor.Black * (0xaa / 255f);

        // COLOR.COMPL.BORDER_TOP.
        public static readonly UIColor BorderTop = UIColor.Cyan;

        // COLOR.COMPL.BORDER_LEFT.
        public static readonly UIColor BorderLeft = UIColor.CornflowerBlue;

        // COLOR.COMPL.TEXT, unselected.
        public static readonly UIColor Text = UIColor.LightGray;

        // COLOR.COMPL.TEXT, selected.
        public static readonly UIColor SelectedText = UIColor.White;

        // COLOR.COMPL.SEL_BG: 0x22 / 255.
        public static readonly UIColor SelectedBackground = UIColor.Wheat * (0x22 / 255f);

        // COLOR.COMPL.SEL_BAR.
        public static readonly UIColor SelectedBar = UIColor.Wheat;
    }

    // debug-map overlay drawn over the level editor.
    public static class DebugMap
    {
        // player name colour; the marker square uses the player's own hair colour.
        public static readonly UIColor Name = UIColor.White;
    }

    // player list panel and rows.
    public static class PlayerList
    {
        // COLOR.PL.BG: 0xcc / 255.
        public static readonly UIColor Background = UIColor.Black * (0xcc / 255f);

        // COLOR.PL.BORDER_TOP, thickness 3.
        public static readonly UIColor BorderTop = UIColor.CornflowerBlue;

        // COLOR.PL.BORDER_LEFT, thickness 3.
        public static readonly UIColor BorderLeft = UIColor.Cyan;

        // COLOR.PL.HEADER.
        public static readonly UIColor Header = UIColor.Yellow;

        // COLOR.PL.STRIPE_EVEN: (0, 0, 0, 0x22).
        public static readonly UIColor StripeEven = UIColor.FromBytes(0x00, 0x00, 0x00, 0x22);

        // COLOR.PL.STRIPE_ODD: (0x22, 0x22, 0x22, 0x88).
        public static readonly UIColor StripeOdd = UIColor.FromBytes(0x22, 0x22, 0x22, 0x88);

        // COLOR.PL.PING.
        public static readonly UIColor Ping = UIColor.LightGray;

        // COLOR.PL.ROOM (room name and the separating colon).
        public static readonly UIColor Room = UIColor.LightGray;

        // COLOR.PL.ICON: tint for every status icon.
        public static readonly UIColor Icon = UIColor.White;

        // PL.ROW.DEFAULT_COLOR: unknown maps and sides.
        public static readonly UIColor UnknownMap = UIColor.LightGray;
    }
}
