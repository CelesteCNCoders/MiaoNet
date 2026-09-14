using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// one child plus padding, with an optional background and border.
// padding comes from UiStyle.Padding; the box's own rect covers the padded area, so its
// painted background matches the outer bounds.
public class BoxNode : SingleChildNode
{
    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        EdgeInsets padding = Style.Padding;
        UiSize childSize = Child?.Measure(constraints.Deflate(padding)) ?? UiSize.Zero;
        UiSize desired = new(
            childSize.Width + padding.Horizontal,
            childSize.Height + padding.Vertical);
        return constraints.Constrain(desired);
    }

    protected override void OnArrange(UiRect bounds)
    {
        Child?.Arrange(bounds.Deflate(Style.Padding));
    }

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
    {
        UiRect rect = Style.PixelSnap ? Bounds.Snap() : Bounds;

        if (Style.Background is UiColor background)
        {
            canvas.FillRect(rect, background * opacity);
        }

        PaintBorder(canvas, rect, opacity);
    }

    // uniform border on all four edges. subclasses override this when a panel needs a different
    // color or thickness per edge, e.g. the player list channel headers.
    protected virtual void PaintBorder(IUiCanvas canvas, UiRect rect, float opacity)
    {
        float borderWidth = Style.BorderWidth ?? 0f;
        if (borderWidth <= 0f || Style.BorderColor is not UiColor borderColor)
        {
            return;
        }

        UiColor color = borderColor * opacity;
        canvas.FillRect(new UiRect(rect.X, rect.Y, rect.Width, borderWidth), color);
        canvas.FillRect(new UiRect(rect.X, rect.Bottom - borderWidth, rect.Width, borderWidth), color);
        canvas.FillRect(new UiRect(rect.X, rect.Y, borderWidth, rect.Height), color);
        canvas.FillRect(new UiRect(rect.Right - borderWidth, rect.Y, borderWidth, rect.Height), color);
    }
}
