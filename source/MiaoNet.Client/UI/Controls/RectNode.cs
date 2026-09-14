using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// a solid rect that just takes whatever size the layout gives it -- zebra stripes, borders,
// fixed-width gaps (give it Style.Width or a flex share).
public sealed class RectNode : UiNode
{
    protected override UiSize OnMeasure(BoxConstraints constraints)
        => constraints.Constrain(UiSize.Zero);

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
    {
        if (Style.Background is not UiColor background)
        {
            return;
        }

        UiRect rect = Style.PixelSnap ? Bounds.Snap() : Bounds;
        canvas.FillRect(rect, background * opacity);
    }
}
