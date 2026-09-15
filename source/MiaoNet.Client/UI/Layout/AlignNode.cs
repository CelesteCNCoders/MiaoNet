using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// places a single child inside its own box according to Alignment.
// the node itself is the child's size unless its style fixes a size or the incoming
// constraints are tight.
public sealed class AlignNode : SingleChildNode
{
    public UIAlignment Alignment { get; set; } = UIAlignment.Center;

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        UISize childSize = Child?.Measure(constraints.Loosen()) ?? UISize.Zero;
        return constraints.Constrain(childSize);
    }

    protected override void OnArrange(UIRect bounds)
    {
        if (Child is null)
        {
            return;
        }

        UISize size = Child.MeasuredSize;
        UIOffset offset = Alignment.OffsetFor(bounds.Size, size);
        Child.Arrange(new UIRect(bounds.X + offset.X, bounds.Y + offset.Y, size.Width, size.Height));
    }
}
