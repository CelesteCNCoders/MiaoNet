using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// empty node that absorbs whatever space is left along its parent's main axis.
// zero-sized unless a FlexNode hands it a flex share.
public sealed class SpacerNode : UINode
{
    public SpacerNode()
    {
        Flex = 1f;
    }

    protected override UISize OnMeasure(BoxConstraints constraints)
        => constraints.Constrain(UISize.Zero);
}
