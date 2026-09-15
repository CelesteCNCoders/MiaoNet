using Celeste.Mod.MiaoNet.UI.Geometry;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// drawing surface the ui tree paints into. the backend owns the sprite batch lifetime and
// converts UIColor to the game's color type; the layout core never touches XNA directly.
public interface IUICanvas
{
    void FillRect(UIRect rect, UIColor color);

    void DrawLine(UIOffset from, UIOffset to, UIColor color, float thickness);

    // intersects with the current clip region.
    void PushClip(UIRect rect);

    // restores the clip saved by the matching pushclip.
    void PopClip();
}
