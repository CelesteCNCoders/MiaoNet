using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// places a single child inside its own box according to Alignment.
// the node itself is the child's size unless its style fixes a size or the incoming
// constraints are tight.
public sealed class AlignNode : SingleChildNode
{
    public UiAlignment Alignment { get; set; } = UiAlignment.Center;

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        UiSize childSize = Child?.Measure(constraints.Loosen()) ?? UiSize.Zero;
        return constraints.Constrain(childSize);
    }

    protected override void OnArrange(UiRect bounds)
    {
        if (Child is null)
        {
            return;
        }

        UiSize size = Child.MeasuredSize;
        UiOffset offset = Alignment.OffsetFor(bounds.Size, size);
        Child.Arrange(new UiRect(bounds.X + offset.X, bounds.Y + offset.Y, size.Width, size.Height));
    }
}
