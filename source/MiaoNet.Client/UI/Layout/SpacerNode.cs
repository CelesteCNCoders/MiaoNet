using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// empty node that absorbs whatever space is left along its parent's main axis.
// zero-sized unless a FlexNode hands it a flex share.
public sealed class SpacerNode : UiNode
{
    public SpacerNode()
    {
        Flex = 1f;
    }

    protected override UiSize OnMeasure(BoxConstraints constraints)
        => constraints.Constrain(UiSize.Zero);
}
