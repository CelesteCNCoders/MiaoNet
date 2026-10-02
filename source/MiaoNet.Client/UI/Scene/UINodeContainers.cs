using System;
using System.Collections.Generic;

namespace Celeste.Mod.MiaoNet.UI.Scene;

// base for nodes with exactly one child.
public abstract class SingleChildNode : UINode
{
    private static readonly UINode[] NoChildren = Array.Empty<UINode>();
    private readonly UINode[] singleChildBuffer = new UINode[1];
    private UINode? child;

    public UINode? Child
    {
        get => child;
        set
        {
            if (ReferenceEquals(child, value))
            {
                return;
            }

            if (child is not null)
            {
                child.Parent = null;
            }

            child = value;

            if (child is not null)
            {
                child.Parent = this;
            }

            InvalidateMeasure();
        }
    }

    public override IReadOnlyList<UINode> Children
    {
        get
        {
            if (child is null)
            {
                return NoChildren;
            }

            singleChildBuffer[0] = child;
            return singleChildBuffer;
        }
    }
}

// base for nodes with an ordered list of children.
public abstract class MultiChildNode : UINode
{
    private readonly List<UINode> items = [];

    public IReadOnlyList<UINode> Items => items;

    public override IReadOnlyList<UINode> Children => items;

    public void Add(UINode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent = this;
        items.Add(child);
        InvalidateMeasure();
    }

    public void AddRange(IEnumerable<UINode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        foreach (UINode child in children)
        {
            Add(child);
        }
    }

    public void SetItems(IEnumerable<UINode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        Clear();
        AddRange(children);
    }

    public void RemoveAt(int index)
    {
        items[index].Parent = null;
        items.RemoveAt(index);
        InvalidateMeasure();
    }

    public void Clear()
    {
        foreach (UINode child in items)
        {
            child.Parent = null;
        }
        items.Clear();
        InvalidateMeasure();
    }
}
