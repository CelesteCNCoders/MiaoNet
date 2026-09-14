using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// a solid rect that just takes whatever size the layout gives it -- zebra stripes, borders,
// fixed-width gaps (give it Style.Width or a flex share).
public sealed class RectNode : UINode
{
    protected override UISize OnMeasure(BoxConstraints constraints)
        => constraints.Constrain(UISize.Zero);

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        if (Style.Background is not UIColor background)
        {
            return;
        }

        UIRect rect = Style.PixelSnap ? Bounds.Snap() : Bounds;
        canvas.FillRect(rect, background * opacity);
    }
}
