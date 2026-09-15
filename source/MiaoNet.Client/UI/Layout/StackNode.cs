using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// overlapping layout: every child is measured against the same space, and the stack ends up
// as big as its largest child. used for the root layers of the screen ui.
public sealed class StackNode : MultiChildNode
{
    // used when a child is smaller than the stack
    public UIAlignment Alignment { get; set; } = UIAlignment.TopLeft;

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        BoxConstraints childConstraints = constraints.Loosen();
        float width = 0f;
        float height = 0f;

        foreach (UINode child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            UISize size = child.Measure(childConstraints);
            width = MathF.Max(width, size.Width);
            height = MathF.Max(height, size.Height);
        }

        return constraints.Constrain(new UISize(width, height));
    }

    protected override void OnArrange(UIRect bounds)
    {
        foreach (UINode child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            UISize size = child.MeasuredSize;
            UIOffset offset = Alignment.OffsetFor(bounds.Size, size);
            child.Arrange(new UIRect(bounds.X + offset.X, bounds.Y + offset.Y, size.Width, size.Height));
        }
    }
}
