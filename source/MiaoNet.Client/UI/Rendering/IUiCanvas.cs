using Celeste.Mod.MiaoNet.UI.Geometry;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// drawing surface the ui tree paints into. the backend owns the sprite batch lifetime and
// converts UiColor to the game's color type; the layout core never touches XNA directly.
public interface IUiCanvas
{
    void FillRect(UiRect rect, UiColor color);

    void DrawLine(UiOffset from, UiOffset to, UiColor color, float thickness);

    // intersects with the current clip region.
    void PushClip(UiRect rect);

    // restores the clip saved by the matching pushclip.
    void PopClip();
}
