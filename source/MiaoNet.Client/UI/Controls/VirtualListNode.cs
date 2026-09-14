using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// feeds items to a VirtualListNode
public interface IVirtualListDataSource
{
    int Count { get; }

    // builds a node for an item that just entered the window
    UINode BuildItem(int index);

    // stable identity of an item. nodes are reused while their key stays in the window, so
    // scrolling does not rebuild the items that stay visible.
    object GetItemKey(int index);
}

// a vertically virtualized list with a fixed item extent: only the items intersecting the
// viewport (plus a little overscan) are mounted and painted.
//
// this is the only place in the UI layer that reconciles nodes by identity, and I want to keep
// it that way -- the rest of the tree is retained and updated in place, there is no global
// reconciliation pass.
public class VirtualListNode : UINode
{
    private readonly Dictionary<object, UINode> mounted = [];
    private readonly List<UINode> window = [];

    private IVirtualListDataSource? dataSource;

    public IVirtualListDataSource? DataSource
    {
        get => dataSource;
        set
        {
            if (ReferenceEquals(dataSource, value))
            {
                return;
            }

            dataSource = value;
            Reset();
        }
    }

    // height of one item, excluding Spacing
    public float ItemExtent { get; set; } = 1f;

    public float Spacing { get; set; }

    public int OverscanItems { get; set; } = 1;

    // when set, an item's paint opacity is the fraction of it visible in the viewport: linear
    // edge fade, dimming the one message straddling an edge instead of clipping it.
    public bool FadeItemsAtEdges { get; set; }

    // scroll position measured from the top of the content
    public float Offset { get; set; }

    public UISize ContentSize { get; private set; }

    public int FirstVisibleIndex { get; private set; } = -1;

    public int LastVisibleIndex { get; private set; } = -1;

    // distance between the tops of two adjacent items
    public float Step => ItemExtent + Spacing;

    public float MaxScroll => MathF.Max(0f, ContentSize.Height - Bounds.Height);

    public override IReadOnlyList<UINode> Children => window;

    public float ClampOffset(float value) => Math.Clamp(value, 0f, MaxScroll);

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        int count = dataSource?.Count ?? 0;
        float contentHeight = count <= 0 ? 0f : (count * Step) - Spacing;

        // The width comes from the layout (Style.Width or a tight constraint); item widths are
        // content-derived and only measured for the items actually mounted.
        float width = constraints.Constrain(new UISize(constraints.MaxWidth, 0f)).Width;
        ContentSize = new UISize(width, contentHeight);

        float height = float.IsInfinity(constraints.MaxHeight)
            ? contentHeight
            : MathF.Min(contentHeight, constraints.MaxHeight);
        return constraints.Constrain(new UISize(width, height));
    }

    protected override void OnArrange(UIRect bounds)
    {
        int count = dataSource?.Count ?? 0;
        if (count <= 0)
        {
            FirstVisibleIndex = -1;
            LastVisibleIndex = -1;
            UnmountAll();
            return;
        }

        float step = Step;
        int first = Math.Clamp((int)MathF.Floor(Offset / step), 0, count - 1);
        int last = Math.Clamp((int)MathF.Floor((Offset + MathF.Max(0f, bounds.Height - 0.001f)) / step), first, count - 1);
        FirstVisibleIndex = first;
        LastVisibleIndex = last;

        int from = Math.Max(0, first - OverscanItems);
        int to = Math.Min(count - 1, last + OverscanItems);
        MountWindow(from, to);

        // Item widths are content-derived: a chat message's background spans its own text, not the
        // viewport. Only the mounted items are measured, so this stays proportional to the window.
        var itemConstraints = new BoxConstraints(0f, float.PositiveInfinity, ItemExtent, ItemExtent);
        for (int i = from; i <= to; i++)
        {
            UINode node = window[i - from];

            // Push per-frame state BEFORE measuring: it may update metrics that the measurement
            // depends on. A freshly built node still carries its type's defaults, so measuring
            // first would lay it out with the wrong size for one frame and visibly jump.
            OnItemMounted(node, i);

            UISize size = node.Measure(itemConstraints);

            float y = bounds.Y + (i * step) - Offset;
            node.Arrange(new UIRect(bounds.X, y, size.Width, size.Height));

            if (FadeItemsAtEdges)
            {
                node.Opacity = VisibleFraction(node.Bounds, bounds);
            }
        }
    }

    protected override UIRect? CullRectFor(UIRect? inherited)
    {
        UIRect own = Bounds;
        return inherited is { } outer
            ? new UIRect(
                MathF.Max(outer.X, own.X),
                MathF.Max(outer.Y, own.Y),
                MathF.Max(0f, MathF.Min(outer.Right, own.Right) - MathF.Max(outer.X, own.X)),
                MathF.Max(0f, MathF.Min(outer.Bottom, own.Bottom) - MathF.Max(outer.Y, own.Y)))
            : own;
    }

    // called for every mounted item on every layout pass, before the item is measured. push
    // current settings and per-frame state here -- anything that affects measurement has to go
    // through this hook, not build time.
    protected virtual void OnItemMounted(UINode node, int index)
    {
    }

    // discards all mounted nodes, e.g. when the data source is replaced
    protected void Reset()
    {
        UnmountAll();
        ContentSize = UISize.Zero;
        Offset = 0f;
        InvalidateMeasure();
    }

    private void MountWindow(int from, int to)
    {
        var wanted = new HashSet<object>();
        var next = new List<UINode>(to - from + 1);

        for (int i = from; i <= to; i++)
        {
            object key = dataSource!.GetItemKey(i);
            if (!mounted.TryGetValue(key, out UINode? node))
            {
                node = dataSource.BuildItem(i);
                node.Parent = this;
                mounted[key] = node;
            }

            wanted.Add(key);
            next.Add(node);
        }

        // Drop nodes whose items left the window, then install the new window in order.
        foreach (object key in mounted.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            mounted[key].Parent = null;
            mounted.Remove(key);
        }

        window.Clear();
        window.AddRange(next);
    }

    private void UnmountAll()
    {
        foreach (UINode node in mounted.Values)
        {
            node.Parent = null;
        }

        mounted.Clear();
        window.Clear();
    }

    private static float VisibleFraction(UIRect item, UIRect viewport)
    {
        if (item.Height <= 0f)
        {
            return item.Bottom > viewport.Y && item.Y < viewport.Bottom ? 1f : 0f;
        }

        float top = MathF.Max(item.Y, viewport.Y);
        float bottom = MathF.Min(item.Bottom, viewport.Bottom);
        return Math.Clamp(MathF.Max(0f, bottom - top) / item.Height, 0f, 1f);
    }
}
