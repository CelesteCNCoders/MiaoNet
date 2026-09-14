using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;

namespace Celeste.Mod.MiaoNet.UI.Scene;

// owns the node tree and drives layout, painting, hit testing and focus.
// one runtime serves the whole screen ui; chat and player list are siblings in the same tree.
// unlike the reference framework there's no per-frame setroot: the tree is retained and only
// rebuilt when the whole ui opens or closes. data changes flow through node update methods
// and invalidatemeasure.
public sealed class UiRoot
{
    private UiNode? root;

    public UiNode? Root => root;

    public UiNode? FocusedNode { get; private set; }

    // logical size used for the last layout.
    public UiSize Size { get; private set; }

    // replaces the whole tree. only for opening or closing the ui, never a per-frame path.
    public void SetRoot(UiNode? node)
    {
        if (ReferenceEquals(root, node))
        {
            return;
        }

        root = node;
        FocusedNode = null;
        root?.InvalidateMeasure();
    }

    // measures and arranges the tree under a tight screen-sized constraint.
    public void Layout(float width, float height)
    {
        Size = new UiSize(width, height);
        if (root is null)
        {
            return;
        }

        root.Measure(BoxConstraints.Tight(width, height));
        root.Arrange(new UiRect(0f, 0f, width, height));
    }

    public void Paint(IUiCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        root?.PaintTree(canvas, 1f);
    }

    public UiNode? HitTest(UiOffset point) => root?.HitTest(point);

    public void RequestFocus(UiNode? node) => FocusedNode = node;

    public void ClearFocus() => FocusedNode = null;
}
