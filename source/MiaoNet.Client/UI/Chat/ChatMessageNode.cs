using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// one chat line. runs are laid out inline, each measured and advanced by its own width, drawing a
// single line with per-segment colors and decorations.
//
// y is anchored at the row's bottom: the background covers the row rect and the
// text baseline sits MessagePaddingY above the bottom edge.
public sealed class ChatMessageNode : UINode
{
    private readonly ITextRenderer renderer;
    private ChatMessageRow row;

    public ChatMessageNode(ITextRenderer renderer, ChatMessageRow row)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(row);
        this.renderer = renderer;
        this.row = row;
    }

    public ChatMessageRow Row => row;

    // from the ChatMessagePadding setting.
    public float MessagePaddingY { get; set; }

    public float LineHeight { get; set; }

    public float Scale { get; set; } = 1f;

    // from ChatBackgroundOpacity.
    public float BackgroundOpacity { get; set; } = 0.5f;

    // from ChatTextOpacity.
    public float TextOpacity { get; set; } = 1f;

    // per-message fade from the controller; 0 means fully elapsed.
    public float Fade { get; set; } = 1f;

    public float CounterPopProgress { get; set; } = 1f;

    public float CounterAnimClock { get; set; }

    public float MessageLineHeight => LineHeight + (2f * MessagePaddingY);

    // pushes the current settings onto the node. metrics that affect measurement only invalidate
    // when they actually changed, so this is safe to call every frame.
    public void ApplyMetrics(
        float messagePaddingY,
        float lineHeight,
        float scale,
        float backgroundOpacity,
        float textOpacity)
    {
        bool measureDirty = MessagePaddingY != messagePaddingY
            || LineHeight != lineHeight
            || Scale != scale;

        MessagePaddingY = messagePaddingY;
        LineHeight = lineHeight;
        Scale = scale;
        BackgroundOpacity = backgroundOpacity;
        TextOpacity = textOpacity;

        if (measureDirty)
        {
            InvalidateMeasure();
        }
    }

    // fixed width timestamp cell, reserved even for shorter stamps.
    public float TimeCellWidth
        => row.TimeText is null ? 0f : (ChatLayout.TimeTextWidthRatio * LineHeight) + (2f * ChatLayout.TimeTextPaddingX);

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        TextStyle style = RunStyle(UIColor.White);
        float width = TimeCellWidth;

        foreach (ChatTextRun run in row.Runs)
        {
            width += renderer.Measure(run.Text, style).Width;
        }

        if (row.RepeatCount > 1)
        {
            width += CounterWidth();
        }

        return constraints.Constrain(new UISize(width + (2f * ChatLayout.MessagePaddingX), MessageLineHeight));
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        if (Fade <= 0f)
        {
            return;
        }

        // opacity is the list's edge fade, Fade is this message's own timer.
        float alpha = opacity * Fade;
        canvas.FillRect(
            Bounds.Snap(),
            MiaoNetUITheme.Chat.Background * (alpha * BackgroundOpacity));

        float textAlpha = alpha * TextOpacity;
        float baseline = Bounds.Bottom - MessagePaddingY;
        float x = Bounds.X + ChatLayout.MessagePaddingX;

        if (row.TimeText is { Length: > 0 } time)
        {
            renderer.Draw(
                canvas,
                time,
                new UIOffset(x + ChatLayout.TimeTextPaddingX, baseline),
                RunStyle(MiaoNetUITheme.Chat.Time, textAlpha, VerticalAnchor.Bottom));
            x += TimeCellWidth;
        }

        float runsStart = x;
        foreach (ChatTextRun run in row.Runs)
        {
            TextStyle style = RunStyle(run.Color, textAlpha, VerticalAnchor.Bottom) with
            {
                Decorations = run.Decorations,
            };
            renderer.Draw(canvas, run.Text, new UIOffset(x, baseline), style);
            x += renderer.Measure(run.Text, style).Width;
        }

        if (row.RepeatCount > 1)
        {
            PaintCounter(canvas, runsStart + MessageBodyWidth(), textAlpha);
        }
    }

    private float MessageBodyWidth()
    {
        TextStyle style = RunStyle(UIColor.White);
        float width = 0f;
        foreach (ChatTextRun run in row.Runs)
        {
            width += renderer.Measure(run.Text, style).Width;
        }
        return width;
    }

    private float CounterScale()
    {
        float progress = Math.Clamp(CounterPopProgress, 0f, 1f);
        return FoldCounter.GetScale(row.RepeatCount)
            + FoldCounter.GetPopScale(ChatLayout.ElasticOut(progress));
    }

    private float CounterWidth()
    {
        // measure with the pop scale and max shake so the row always covers the counter.
        float scale = Scale * CounterScale();
        float text = renderer.Measure(CounterText(), RunStyle(UIColor.White) with { Scale = scale }).Width;
        float gap = ChatLayout.CounterGap * Scale;
        return gap + text + (FoldCounter.GetShakeAmplitude(row.RepeatCount) * Scale);
    }

    private string CounterText() => $"X{row.RepeatCount}";

    private void PaintCounter(IUICanvas canvas, float x, float textAlpha)
    {
        float scale = Scale * CounterScale();
        float gap = ChatLayout.CounterGap * Scale;
        float shake = FoldCounter.GetShakeAmplitude(row.RepeatCount) * Scale;

        var offset = new UIOffset(0f, 0f);
        if (shake > 0f)
        {
            offset = new UIOffset(
                ((Random.Shared.NextSingle() * 2f) - 1f) * shake,
                ((Random.Shared.NextSingle() * 2f) - 1f) * shake);
        }

        RgbColor rgb = FoldCounter.GetColor(row.RepeatCount, CounterAnimClock);
        var color = new UIColor(rgb.R, rgb.G, rgb.B, 1f);

        // centred on the row so the pop animation grows symmetrically.
        float centerY = Bounds.Y + (MessageLineHeight * 0.5f);
        renderer.Draw(
            canvas,
            CounterText(),
            new UIOffset(x + gap + offset.X, centerY + offset.Y),
            new TextStyle
            {
                Scale = scale,
                LineHeight = LineHeight,
                Color = color * textAlpha,
                HorizontalAnchor = HorizontalAnchor.Left,
                VerticalAnchor = VerticalAnchor.Center,
            });
    }

    private TextStyle RunStyle(
        UIColor color,
        float alpha = 1f,
        VerticalAnchor verticalAnchor = VerticalAnchor.Top)
        => new()
        {
            Scale = Scale,
            LineHeight = LineHeight,
            Color = color * alpha,
            HorizontalAnchor = HorizontalAnchor.Left,
            VerticalAnchor = verticalAnchor,
        };
}
