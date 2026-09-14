using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// drawable texture handle. abstracts the game's texture type so icon nodes stay free of XNA
// and remain unit testable.
public interface IUiTexture
{
    float Width { get; }

    float Height { get; }

    // position is the anchor point; the horizontal/vertical anchors follow the same convention
    // as ITextRenderer.
    void Draw(
        IUiCanvas canvas,
        UiOffset position,
        UiColor tint,
        float scale,
        HorizontalAnchor horizontalAnchor = HorizontalAnchor.Left,
        VerticalAnchor verticalAnchor = VerticalAnchor.Top);
}
