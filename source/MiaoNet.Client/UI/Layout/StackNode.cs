using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// overlapping layout: every child is measured against the same space, and the stack ends up
// as big as its largest child. used for the root layers of the screen ui.
public sealed class StackNode : MultiChildNode
{
    // used when a child is smaller than the stack
    public UiAlignment Alignment { get; set; } = UiAlignment.TopLeft;

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        BoxConstraints childConstraints = constraints.Loosen();
        float width = 0f;
        float height = 0f;

        foreach (UiNode child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            UiSize size = child.Measure(childConstraints);
            width = MathF.Max(width, size.Width);
            height = MathF.Max(height, size.Height);
        }

        return constraints.Constrain(new UiSize(width, height));
    }

    protected override void OnArrange(UiRect bounds)
    {
        foreach (UiNode child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            UiSize size = child.MeasuredSize;
            UiOffset offset = Alignment.OffsetFor(bounds.Size, size);
            child.Arrange(new UiRect(bounds.X + offset.X, bounds.Y + offset.Y, size.Width, size.Height));
        }
    }
}
