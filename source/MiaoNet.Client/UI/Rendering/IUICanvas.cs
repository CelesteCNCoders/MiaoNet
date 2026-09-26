using Celeste.Mod.MiaoNet.UI.Geometry;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// drawing surface the ui tree paints into. the backend owns the sprite batch lifetime and
// knows about the game's drawing state; the tree only sees rects and colors.
public interface IUICanvas
{
    void FillRect(UIRect rect, Color color);

    void DrawLine(Vector2 from, Vector2 to, Color color, float thickness);

    // intersects with the current clip region.
    void PushClip(UIRect rect);

    // restores the clip saved by the matching pushclip.
    void PopClip();
}
