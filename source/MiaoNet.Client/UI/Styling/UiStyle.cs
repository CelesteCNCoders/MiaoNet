using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;

namespace Celeste.Mod.MiaoNet.UI.Styling;

// visual style of a node: sizing bounds, padding, colors and text defaults.
// sizing fields feed measure as constraints, so width+height set makes a fixed-size box.
// no margin on purpose: spacing is expressed as parent padding or explicit layout
// constants, so a separate margin channel would just be unused complexity.
// add it when something actually needs it.
public sealed class UiStyle
{
    // a fresh default style; each node gets its own instance so nobody mutates a shared one.
    public static UiStyle Default => new();

    public float? Width { get; set; }

    public float? Height { get; set; }

    public float? MinWidth { get; set; }

    public float? MaxWidth { get; set; }

    public float? MinHeight { get; set; }

    public float? MaxHeight { get; set; }

    public EdgeInsets Padding { get; set; } = EdgeInsets.Zero;

    public UiColor? Background { get; set; }

    public UiColor? Foreground { get; set; }

    public UiColor? BorderColor { get; set; }

    public float? BorderWidth { get; set; }

    // multiplied into the inherited opacity for this node and its subtree.
    public float? Opacity { get; set; }

    // default text scale for text nodes in this subtree.
    public float? FontScale { get; set; }

    // default line height for text nodes in this subtree.
    public float? LineHeight { get; set; }

    public ITextRenderer? TextRenderer { get; set; }

    // snap painted geometry to whole pixels. chat message backgrounds and tab bars snap,
    // while the input box, completion popup and player list do not (GLOBAL.SNAP.*).
    public bool PixelSnap { get; set; }
}
