using System;
using System.Collections.Generic;

namespace Celeste.Mod.MiaoNet.UI.Scene;

// base for nodes with exactly one child.
public abstract class SingleChildNode : UiNode
{
    private static readonly UiNode[] NoChildren = Array.Empty<UiNode>();
    private readonly UiNode[] singleChildBuffer = new UiNode[1];
    private UiNode? child;

    public UiNode? Child
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

    public override IReadOnlyList<UiNode> Children
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
public abstract class MultiChildNode : UiNode
{
    private readonly List<UiNode> items = [];

    public IReadOnlyList<UiNode> Items => items;

    public override IReadOnlyList<UiNode> Children => items;

    public void Add(UiNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent = this;
        items.Add(child);
        InvalidateMeasure();
    }

    public void AddRange(IEnumerable<UiNode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        foreach (UiNode child in children)
        {
            Add(child);
        }
    }

    public void SetItems(IEnumerable<UiNode> children)
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
        foreach (UiNode child in items)
        {
            child.Parent = null;
        }
        items.Clear();
        InvalidateMeasure();
    }
}
