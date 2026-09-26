using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// drawable texture handle. abstracts the game's texture type so icon nodes can be painted in
// tests without the game.
public interface IUITexture
{
    float Width { get; }

    float Height { get; }

    // position is the anchor point; the horizontal/vertical anchors follow the same convention
    // as ITextRenderer.
    void Draw(
        IUICanvas canvas,
        Vector2 position,
        Color tint,
        float scale,
        HorizontalAnchor horizontalAnchor = HorizontalAnchor.Left,
        VerticalAnchor verticalAnchor = VerticalAnchor.Top);
}
