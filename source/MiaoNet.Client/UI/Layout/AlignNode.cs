using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// places a single child inside its own box according to Alignment.
//
// sizing follows flutter's RenderPositionedBox: an axis fills the box it is offered, unless a factor
// pins it to the child or that axis is unbounded. so an alignment host anchored to the screen needs
// no size pushed into it -- it takes the box from the constraints it is measured with, and a window
// resize is just another measurement.
public sealed class AlignNode : SingleChildNode
{
    public UIAlignment Alignment { get; set; } = UIAlignment.Center;

    // width = child width * this, instead of filling the offered width. null fills.
    public float? WidthFactor { get; set; }

    // height = child height * this, instead of filling the offered height. null fills.
    public float? HeightFactor { get; set; }

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        UISize childSize = Child?.Measure(constraints.Loosen()) ?? UISize.Zero;

        // an unbounded axis can only shrink-wrap, and a factor asks for it explicitly. everything
        // else fills, which Constrain then clamps into the bounds -- so the infinite box needs no
        // special case here, the branch itself is the check for it.
        bool shrinkWrapWidth = WidthFactor is not null || float.IsInfinity(constraints.MaxWidth);
        bool shrinkWrapHeight = HeightFactor is not null || float.IsInfinity(constraints.MaxHeight);

        return constraints.Constrain(new UISize(
            shrinkWrapWidth ? childSize.Width * (WidthFactor ?? 1f) : float.PositiveInfinity,
            shrinkWrapHeight ? childSize.Height * (HeightFactor ?? 1f) : float.PositiveInfinity));
    }

    protected override void OnArrange(UIRect bounds)
    {
        if (Child is null)
        {
            return;
        }

        UISize size = Child.MeasuredSize;
        Vector2 offset = Alignment.OffsetFor(bounds.Size, size);
        Child.Arrange(new UIRect(bounds.X + offset.X, bounds.Y + offset.Y, size.Width, size.Height));
    }
}
