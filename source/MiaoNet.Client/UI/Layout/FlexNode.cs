using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Layout;

// linear stack of children along one axis — the HBox / VBox equivalent.
// the only authority for child positions, so no caller computes coordinates by hand.
public sealed class FlexNode : MultiChildNode
{
    public FlexAxis Axis { get; set; } = FlexAxis.Vertical;

    // gap between adjacent children along the main axis
    public float Spacing { get; set; }

    public MainAxisAlignment MainAxisAlignment { get; set; } = MainAxisAlignment.Start;

    public CrossAxisAlignment CrossAxisAlignment { get; set; } = CrossAxisAlignment.Stretch;

    private bool IsHorizontal => Axis == FlexAxis.Horizontal;

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        bool horizontal = IsHorizontal;
        float mainMax = horizontal ? constraints.MaxWidth : constraints.MaxHeight;
        float crossMax = horizontal ? constraints.MaxHeight : constraints.MaxWidth;
        bool stretch = CrossAxisAlignment == CrossAxisAlignment.Stretch && !float.IsInfinity(crossMax);

        List<UINode> visible = VisibleChildren();
        if (visible.Count == 0)
        {
            return constraints.Constrain(UISize.Zero);
        }

        float totalInflexMain = 0f;
        float totalFlex = 0f;
        float maxCross = 0f;

        // pass 1: inflexible children decide how much main-axis space is left for flexible ones.
        foreach (UINode child in visible)
        {
            if (child.Flex > 0f)
            {
                totalFlex += child.Flex;
                continue;
            }

            UISize size = child.Measure(ChildConstraints(horizontal, mainMax, crossMax, stretch, mainOverride: null));
            totalInflexMain += horizontal ? size.Width : size.Height;
            maxCross = MathF.Max(maxCross, horizontal ? size.Height : size.Width);
        }

        float spacing = Spacing * (visible.Count - 1);
        float remaining = MathF.Max(0f, mainMax - totalInflexMain - spacing);
        float totalMain = totalInflexMain;

        // pass 2: divide the remaining main-axis space by flex weight.
        if (totalFlex > 0f)
        {
            foreach (UINode child in visible)
            {
                if (child.Flex <= 0f)
                {
                    continue;
                }

                float allocated = remaining * (child.Flex / totalFlex);
                UISize size = child.Measure(
                    ChildConstraints(horizontal, mainMax, crossMax, stretch, mainOverride: allocated));
                totalMain += horizontal ? size.Width : size.Height;
                maxCross = MathF.Max(maxCross, horizontal ? size.Height : size.Width);
            }
        }

        totalMain += spacing;
        UISize desired = horizontal
            ? new UISize(totalMain, maxCross)
            : new UISize(maxCross, totalMain);
        return constraints.Constrain(desired);
    }

    protected override void OnArrange(UIRect bounds)
    {
        List<UINode> visible = VisibleChildren();
        if (visible.Count == 0)
        {
            return;
        }

        bool horizontal = IsHorizontal;
        float mainSize = horizontal ? bounds.Width : bounds.Height;
        float crossSize = horizontal ? bounds.Height : bounds.Width;

        float contentMain = 0f;
        foreach (UINode child in visible)
        {
            UISize size = child.MeasuredSize;
            contentMain += horizontal ? size.Width : size.Height;
        }
        contentMain += Spacing * (visible.Count - 1);

        float leftover = MathF.Max(0f, mainSize - contentMain);
        float offset;
        float step = Spacing;

        switch (MainAxisAlignment)
        {
            case MainAxisAlignment.End:
                offset = leftover;
                break;
            case MainAxisAlignment.Center:
                offset = leftover * 0.5f;
                break;
            case MainAxisAlignment.SpaceBetween:
                offset = 0f;
                if (visible.Count > 1)
                {
                    step += leftover / (visible.Count - 1);
                }
                break;
            case MainAxisAlignment.SpaceAround:
            {
                float extra = leftover / visible.Count;
                offset = extra * 0.5f;
                step += extra;
                break;
            }
            case MainAxisAlignment.SpaceEvenly:
            {
                float extra = leftover / (visible.Count + 1);
                offset = extra;
                step += extra;
                break;
            }
            default:
                offset = 0f;
                break;
        }

        float cursor = (horizontal ? bounds.X : bounds.Y) + offset;
        foreach (UINode child in visible)
        {
            UISize size = child.MeasuredSize;
            float childMain = horizontal ? size.Width : size.Height;
            float childCross = horizontal ? size.Height : size.Width;
            float crossOffset = CrossAxisAlignment switch
            {
                CrossAxisAlignment.Center => (crossSize - childCross) * 0.5f,
                CrossAxisAlignment.End => crossSize - childCross,
                _ => 0f,
            };

            UIRect rect = horizontal
                ? new UIRect(cursor, bounds.Y + crossOffset, childMain, childCross)
                : new UIRect(bounds.X + crossOffset, cursor, childCross, childMain);
            child.Arrange(rect);
            cursor += childMain + step;
        }
    }

    private static BoxConstraints ChildConstraints(
        bool horizontal,
        float mainMax,
        float crossMax,
        bool stretch,
        float? mainOverride)
    {
        float crossMin = stretch ? crossMax : 0f;
        float mainMin = mainOverride ?? 0f;
        float mainLimit = mainOverride ?? mainMax;
        if (mainLimit < mainMin)
        {
            mainLimit = mainMin;
        }

        return horizontal
            ? new BoxConstraints(mainMin, mainLimit, crossMin, crossMax)
            : new BoxConstraints(crossMin, crossMax, mainMin, mainLimit);
    }

    private List<UINode> VisibleChildren()
    {
        List<UINode> result = [];
        foreach (UINode child in Children)
        {
            if (child.IsVisible)
            {
                result.Add(child);
            }
        }
        return result;
    }
}
