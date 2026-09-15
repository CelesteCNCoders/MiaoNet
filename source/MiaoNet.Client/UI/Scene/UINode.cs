using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Scene;

// retained ui node: created once and updated in place, we never rebuild the tree every frame.
// measures itself under the constraints its parent hands down, gets arranged at a final rect,
// then paints itself plus its children.
// this is our one layer replacing the reference framework's widget/element/renderobject split.
// since the tree is retained there's no global reconciliation pass; the only place that reuses
// nodes by identity is the virtual list, and that's local to that node.
public abstract class UINode
{
    private static readonly UINode[] NoChildren = Array.Empty<UINode>();

    private UIStyle style = UIStyle.Default;
    private bool measureValid;
    private UISize measured;
    private BoxConstraints lastConstraints;

    // visual style; replacing it invalidates the cached measurement.
    public UIStyle Style
    {
        get => style;
        set
        {
            style = value ?? UIStyle.Default;
            InvalidateMeasure();
        }
    }

    public UINode? Parent { get; internal set; }

    // final rect assigned by the parent during arrange.
    public UIRect Bounds { get; private set; }

    // size from the most recent measure.
    public UISize MeasuredSize { get; private set; }

    private bool isVisible = true;

    // containers skip invisible children when they measure, so a change here invalidates this
    // node and its ancestors.
    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible == value)
            {
                return;
            }

            isVisible = value;
            InvalidateMeasure();
            Parent?.InvalidateMeasure();
        }
    }

    // whether hittest can return this node itself.
    public bool IsHitTestVisible { get; set; } = true;

    // paint-only opacity, multiplied into the inherited one. unlike UIStyle.Opacity it doesn't
    // invalidate measurement, so edge fading can tweak it every frame without a relayout.
    public float Opacity { get; set; } = 1f;

    // flex weight inside a flexnode; 0 means inflexible.
    public float Flex { get; set; }

    public virtual IReadOnlyList<UINode> Children => NoChildren;

    // reuses the cached result while the constraints are unchanged.
    public UISize Measure(BoxConstraints constraints)
    {
        BoxConstraints effective = ApplyStyleSizing(constraints);
        if (measureValid && lastConstraints == effective)
        {
            return measured;
        }

        measured = OnMeasure(effective);
        lastConstraints = effective;
        MeasuredSize = measured;
        measureValid = true;
        return measured;
    }

    public void Arrange(UIRect bounds)
    {
        Bounds = bounds;
        OnArrange(bounds);
    }

    public void InvalidateMeasure()
    {
        if (!measureValid)
        {
            return;
        }

        measureValid = false;
        Parent?.InvalidateMeasure();
    }

    // top-down, painting each visible node at the accumulated opacity.
    public void PaintTree(IUICanvas canvas, float opacity)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        PaintTree(canvas, opacity, null);
    }

    private void PaintTree(IUICanvas canvas, float opacity, UIRect? inheritedCull)
    {
        if (!IsVisible)
        {
            return;
        }

        float nodeOpacity = opacity * (Style.Opacity ?? 1f) * Opacity;
        if (nodeOpacity <= 0f)
        {
            return;
        }

        UIRect? cull = CullRectFor(inheritedCull);

        PaintSelf(canvas, nodeOpacity);
        OnBeforeChildren(canvas);
        foreach (UINode child in Children)
        {
            if (cull is { } rect && !child.Bounds.Intersects(rect))
            {
                continue;
            }

            child.PaintTree(canvas, nodeOpacity, cull);
        }
        OnAfterChildren(canvas);
    }

    // deepest visible node containing the point, if any.
    public virtual UINode? HitTest(UIOffset point)
    {
        if (!IsVisible || !Bounds.Contains(point))
        {
            return null;
        }

        // Children are painted after their parent, so they are on top: test them last-first.
        IReadOnlyList<UINode> children = Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            UINode? hit = children[i].HitTest(point);
            if (hit is not null)
            {
                return hit;
            }
        }

        return IsHitTestVisible ? this : null;
    }

    protected abstract UISize OnMeasure(BoxConstraints constraints);

    protected virtual void OnArrange(UIRect bounds)
    {
    }

    // draw this node only; children are painted by painttree.
    protected virtual void PaintSelf(IUICanvas canvas, float opacity)
    {
    }

    // push graphics state (a clip rect, say) before children paint.
    protected virtual void OnBeforeChildren(IUICanvas canvas)
    {
    }

    // pop whatever onbeforechildren pushed.
    protected virtual void OnAfterChildren(IUICanvas canvas)
    {
    }

    // narrows the paint culling rect for this subtree; scrolling containers intersect it with
    // their viewport so descendants outside get skipped instead of drawn and scissored.
    // returning the inherited value (or null) turns culling off here.
    protected virtual UIRect? CullRectFor(UIRect? inherited) => inherited;

    // folds style sizing fields into the constraints before measuring.
    private BoxConstraints ApplyStyleSizing(BoxConstraints constraints)
    {
        float minWidth = constraints.MinWidth;
        float maxWidth = constraints.MaxWidth;
        float minHeight = constraints.MinHeight;
        float maxHeight = constraints.MaxHeight;

        if (Style.Width is float width)
        {
            minWidth = width;
            maxWidth = width;
        }
        else
        {
            if (Style.MinWidth is float styleMinWidth)
            {
                minWidth = MathF.Max(minWidth, styleMinWidth);
            }
            if (Style.MaxWidth is float styleMaxWidth)
            {
                maxWidth = MathF.Min(maxWidth, styleMaxWidth);
            }
        }

        if (Style.Height is float height)
        {
            minHeight = height;
            maxHeight = height;
        }
        else
        {
            if (Style.MinHeight is float styleMinHeight)
            {
                minHeight = MathF.Max(minHeight, styleMinHeight);
            }
            if (Style.MaxHeight is float styleMaxHeight)
            {
                maxHeight = MathF.Min(maxHeight, styleMaxHeight);
            }
        }

        return new BoxConstraints(minWidth, maxWidth, minHeight, maxHeight);
    }
}
