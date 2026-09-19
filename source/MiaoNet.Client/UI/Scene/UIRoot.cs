using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;

namespace Celeste.Mod.MiaoNet.UI.Scene;

// owns the node tree and drives layout, painting, hit testing and focus.
// one runtime serves the whole screen ui; chat and player list are siblings in the same tree.
// unlike the reference framework there's no per-frame setroot: the tree is retained and only
// rebuilt when the whole ui opens or closes. data changes flow through node update methods
// and invalidatemeasure.
public sealed class UIRoot
{
    private UINode? root;

    public UINode? Root => root;

    public UINode? FocusedNode { get; private set; }

    // logical size used for the last layout.
    public UISize Size { get; private set; }

    // replaces the whole tree. only for opening or closing the ui, never a per-frame path.
    public void SetRoot(UINode? node)
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
        Size = new UISize(width, height);
        if (root is null)
        {
            return;
        }

        root.Measure(BoxConstraints.Tight(width, height));
        root.Arrange(new UIRect(0f, 0f, width, height));
    }

    public void Paint(IUICanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        root?.PaintTree(canvas, 1f);
    }

    public UINode? HitTest(UIOffset point) => root?.HitTest(point);

    public void RequestFocus(UINode? node) => FocusedNode = node;

    public void ClearFocus() => FocusedNode = null;
}
