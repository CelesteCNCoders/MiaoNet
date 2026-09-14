using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// a vertically scrolling viewport over one content child. the child is measured with an
// unbounded height so we know its full extent, then drawn shifted up by Offset.
//
// two things keep content inside the viewport: the canvas clip rect, and per-child culling.
// culling is belt-and-braces on purpose -- it keeps fully off-screen rows from even reaching
// the canvas. the player list viewport is the whole screen, so a mis-sized scissor is easy to
// miss until it is already wrong.
public class ScrollNode : SingleChildNode
{
    private float offset;

    // content offset in pixels; positive scrolls the content up
    public float Offset
    {
        get => offset;
        set => offset = MathF.Max(0f, value);
    }

    // whether to push a clip rect while painting children
    public bool Clip { get; set; } = true;

    // measured size of the content child
    public UISize ContentSize { get; private set; }

    // largest useful offset for the current content and viewport
    public float MaxScroll => MathF.Max(0f, ContentSize.Height - Bounds.Height);

    public float ClampOffset(float value)
        => Math.Clamp(value, 0f, MaxScroll);

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        var contentConstraints = new BoxConstraints(
            constraints.MinWidth,
            constraints.MaxWidth,
            0f,
            float.PositiveInfinity);

        UISize content = Child?.Measure(contentConstraints) ?? UISize.Zero;
        ContentSize = content;

        float height = float.IsInfinity(constraints.MaxHeight)
            ? content.Height
            : MathF.Min(content.Height, constraints.MaxHeight);

        return constraints.Constrain(new UISize(content.Width, height));
    }

    protected override void OnArrange(UIRect bounds)
    {
        Child?.Arrange(new UIRect(bounds.X, bounds.Y - Offset, ContentSize.Width, ContentSize.Height));
    }

    protected override void OnBeforeChildren(IUICanvas canvas)
    {
        if (Clip)
        {
            canvas.PushClip(Bounds);
        }
    }

    protected override void OnAfterChildren(IUICanvas canvas)
    {
        if (Clip)
        {
            canvas.PopClip();
        }
    }

    protected override UIRect? CullRectFor(UIRect? inherited)
    {
        if (!Clip)
        {
            return inherited;
        }

        return inherited is { } outer ? Intersect(outer, Bounds) : Bounds;
    }

    private static UIRect Intersect(UIRect a, UIRect b)
    {
        float x = MathF.Max(a.X, b.X);
        float y = MathF.Max(a.Y, b.Y);
        float right = MathF.Min(a.Right, b.Right);
        float bottom = MathF.Min(a.Bottom, b.Bottom);
        return new UIRect(x, y, MathF.Max(0f, right - x), MathF.Max(0f, bottom - y));
    }
}
