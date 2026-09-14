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
public sealed class CompletionPopupNode : UiNode
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

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        float width = 0f;
        foreach (Completion item in Items)
        {
            width = MathF.Max(width, renderer.Measure(item.Display, TextStyle()).Width);
        }

        return constraints.Constrain(new UiSize(
            width + (2f * Padding),
            (LineHeight * Items.Count) + (2f * Padding)));
    }

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
    {
        if (Items.Count == 0)
        {
            return;
        }

        canvas.FillRect(Bounds, MiaoNetUiTheme.Completion.Background * opacity);
        canvas.FillRect(new UiRect(Bounds.X, Bounds.Y, Bounds.Width, 1f), MiaoNetUiTheme.Completion.BorderTop * opacity);
        canvas.FillRect(
            new UiRect(Bounds.X - LeftBarWidth, Bounds.Y, LeftBarWidth, Bounds.Height),
            MiaoNetUiTheme.Completion.BorderLeft * opacity);

        float baseline = Bounds.Bottom - Padding;
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            bool selected = i == SelectedIndex;

            if (selected)
            {
                canvas.FillRect(
                    new UiRect(Bounds.X, baseline - LineHeight, Bounds.Width, LineHeight),
                    MiaoNetUiTheme.Completion.SelectedBackground * opacity);
                canvas.FillRect(
                    new UiRect(Bounds.X - LeftBarWidth, baseline - LineHeight, LeftBarWidth, LineHeight),
                    MiaoNetUiTheme.Completion.SelectedBar * opacity);
            }

            renderer.Draw(
                canvas,
                Items[i].Display,
                new UiOffset(Bounds.X + Padding, baseline),
                TextStyle() with
                {
                    Color = (selected ? MiaoNetUiTheme.Completion.SelectedText : MiaoNetUiTheme.Completion.Text) * opacity,
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
