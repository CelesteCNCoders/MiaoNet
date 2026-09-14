using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// channel tab strip. the first entry is the "global" tab and is addressed by index -1, ahead of
// the channel tabs.
public sealed class ChatTabBarNode : UiNode
{
    private readonly ITextRenderer renderer;

    public ChatTabBarNode(ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        this.renderer = renderer;
    }

    // title of the first tab, the one ActiveIndex -1 selects.
    public string InitialTitle { get; set; } = string.Empty;

    public IReadOnlyList<string> Tabs { get; set; } = [];

    public int ActiveIndex { get; set; } = -1;

    public float LineHeight { get; set; }

    public float Scale { get; set; } = 1f;

    public int EntryCount => Tabs.Count + 1;

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        float width = 0f;
        for (int i = -1; i < Tabs.Count; i++)
        {
            width += MeasureTab(i) + (2f * ChatLayout.Padding) + ChatLayout.TabGap;
        }

        if (EntryCount > 0)
        {
            width -= ChatLayout.TabGap;
        }

        return constraints.Constrain(new UiSize(width, LineHeight));
    }

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
    {
        float x = Bounds.X;

        for (int i = -1; i < Tabs.Count; i++)
        {
            string title = TitleOf(i);
            float tabWidth = MeasureTab(i) + (2f * ChatLayout.Padding);
            bool isActive = i == ActiveIndex;

            // snap tabs to whole pixels so backgrounds and text line up.
            var rect = new UiRect(
                MathF.Floor(x),
                MathF.Floor(Bounds.Y),
                MathF.Floor(tabWidth),
                LineHeight);

            UiColor backgroundColor = isActive
                ? MiaoNetUiTheme.Tab.ActiveBackground
                : MiaoNetUiTheme.Tab.IdleBackground;
            UiColor textColor = isActive
                ? MiaoNetUiTheme.Tab.ActiveText
                : MiaoNetUiTheme.Tab.IdleText;

            canvas.FillRect(rect, backgroundColor * opacity);

            renderer.Draw(
                canvas,
                title,
                new UiOffset(x + ChatLayout.Padding, Bounds.Bottom),
                new TextStyle
                {
                    Scale = Scale,
                    LineHeight = LineHeight,
                    Color = textColor * opacity,
                    HorizontalAnchor = HorizontalAnchor.Left,
                    VerticalAnchor = VerticalAnchor.Bottom,
                });

            x += tabWidth + ChatLayout.TabGap;
        }
    }

    private string TitleOf(int index) => index == -1 ? InitialTitle : Tabs[index];

    private float MeasureTab(int index)
        => renderer.Measure(TitleOf(index), new TextStyle { Scale = Scale, LineHeight = LineHeight }).Width;
}
