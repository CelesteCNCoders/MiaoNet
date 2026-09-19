using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// completion popup anchored above the input box. items are drawn bottom-up so the last candidate
// ends up closest to the input.
public sealed class CompletionPopupNode : UINode
{
    private readonly ITextRenderer renderer;

    public CompletionPopupNode(ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        this.renderer = renderer;
    }

    private IReadOnlyList<Completion> items = [];

    public IReadOnlyList<Completion> Items
    {
        get => items;
        set
        {
            IReadOnlyList<Completion> next = value ?? [];
            if (ReferenceEquals(items, next))
            {
                return;
            }

            items = next;
            InvalidateMeasure();
        }
    }

    public int SelectedIndex { get; set; } = -1;

    public float LineHeight { get; set; }

    public float Scale { get; set; } = 1f;

    public float Padding { get; set; } = 4f;

    // left bar drawn outside the background.
    public float LeftBarWidth { get; set; } = 3f;

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        float width = 0f;
        foreach (Completion item in Items)
        {
            width = MathF.Max(width, renderer.Measure(item.Display, TextStyle()).Width);
        }

        return constraints.Constrain(new UISize(
            width + (2f * Padding),
            (LineHeight * Items.Count) + (2f * Padding)));
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        if (Items.Count == 0)
        {
            return;
        }

        canvas.FillRect(Bounds, MiaoNetUITheme.Completion.Background * opacity);
        canvas.FillRect(new UIRect(Bounds.X, Bounds.Y, Bounds.Width, 1f), MiaoNetUITheme.Completion.BorderTop * opacity);
        canvas.FillRect(
            new UIRect(Bounds.X - LeftBarWidth, Bounds.Y, LeftBarWidth, Bounds.Height),
            MiaoNetUITheme.Completion.BorderLeft * opacity);

        float baseline = Bounds.Bottom - Padding;
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            bool selected = i == SelectedIndex;

            if (selected)
            {
                canvas.FillRect(
                    new UIRect(Bounds.X, baseline - LineHeight, Bounds.Width, LineHeight),
                    MiaoNetUITheme.Completion.SelectedBackground * opacity);
                canvas.FillRect(
                    new UIRect(Bounds.X - LeftBarWidth, baseline - LineHeight, LeftBarWidth, LineHeight),
                    MiaoNetUITheme.Completion.SelectedBar * opacity);
            }

            renderer.Draw(
                canvas,
                Items[i].Display,
                new UIOffset(Bounds.X + Padding, baseline),
                TextStyle() with
                {
                    Color = (selected ? MiaoNetUITheme.Completion.SelectedText : MiaoNetUITheme.Completion.Text) * opacity,
                });

            baseline -= LineHeight;
        }
    }

    private TextStyle TextStyle() => new()
    {
        Scale = Scale,
        LineHeight = LineHeight,
        HorizontalAnchor = HorizontalAnchor.Left,
        VerticalAnchor = VerticalAnchor.Bottom,
    };
}
