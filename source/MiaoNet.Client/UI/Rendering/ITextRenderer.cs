using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// text backend. MiaoNet draws with its own pixel font; we didn't carry over the reference
// framework's SpriteFont renderer since MiaoNet never used it.
public interface ITextRenderer
{
    // size of text under style, independent of anchoring.
    UiSize Measure(string text, TextStyle style);

    // draws text; position is interpreted through the style's anchors, reproducing the
    // original ui's justify vectors.
    void Draw(IUiCanvas canvas, string text, UiOffset position, TextStyle style);

    bool CanRender(int character, TextStyle style);
}
