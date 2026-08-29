using System.Globalization;

namespace Celeste.Mod.ChatInputBox;

public class ChatItem
{
    private string? dateTimeText;
    private ChatText messageText;

    private const float Margin = 16f;
    private const float Padding = 8f;
    private const float MessageXPadding = 8f;
    private const float MessageYPadding = 8f;

    // width of "00:00:00", the max width of the time text, though we need to avoid hardcoding...
    private const float TimeTextWidthRatio = 3.5625f;
    private const float TimeTextXPadding = 2f;

    // small left gap between the message text and the repeat counter
    private const float RepeatCounterGap = 4f;

    // how many times this message has been folded into, 1 means no counter drawn
    public int RepeatCount { get; set; } = 1;

    public ChatItem(DateTime dateTime, ChatText messageText)
    {
        this.dateTimeText = FormatDateTime(dateTime);
        this.messageText = messageText;
    }

    public ChatItem(ChatText messageText)
    {
        this.messageText = messageText;
    }
    public void render(
        float x, float y, float fade, float backgroundOpacity, float textOpacity,
        IScalelessTextRenderer textRenderer,
        bool fancyCounter, float counterAnimClock, float counterPopProgress
    )
    {
        float lineHeight = textRenderer.LineHeight;
        float messageLineHeight = lineHeight + 2 * MessageYPadding;
        float timeTextMaxWidth = TimeTextWidthRatio * lineHeight + 2 * TimeTextXPadding;
        float messageWidth = MeasureSingleMessage(messageText, textRenderer);
        float lineWidth = messageWidth;
        if (dateTimeText is not null)
            lineWidth += timeTextMaxWidth;

        // only the counter animates, the message text stays static
        string? counterText = null;
        float counterScale = 1f;
        float counterGapWidth = 0f;
        float counterShakeAmplitude = 0f;
        Color counterColor = Color.White;
        if (RepeatCount > 1)
        {
            counterText = $"X{RepeatCount}";
            counterGapWidth = RepeatCounterGap * textRenderer.Scale;
            if (fancyCounter)
            {
                counterScale = FoldCounter.GetScale(RepeatCount)
                    + FoldCounter.GetPopScale(Ease.ElasticOut(Math.Clamp(counterPopProgress, 0f, 1f)));
                counterShakeAmplitude = FoldCounter.GetShakeAmplitude(RepeatCount) * textRenderer.Scale;
                RgbColor rgb = FoldCounter.GetColor(RepeatCount, counterAnimClock);
                counterColor = new Color(rgb.R, rgb.G, rgb.B);
            }
            // measure with pop scale and max shake so the box always covers the counter
            lineWidth += counterGapWidth + textRenderer.Measure(counterText).X * counterScale + counterShakeAmplitude;
        }

        DrawSnappedRect(
            x,
            y - messageLineHeight,
            lineWidth + 2 * MessageXPadding,
            messageLineHeight,
            Color.Black * fade * backgroundOpacity
        );

        float drawAlpha = fade * textOpacity;

        float curX = x + MessageXPadding;
        float curY = y - MessageYPadding;

        if (dateTimeText is not null)
        {
            textRenderer.Draw(dateTimeText, new Vector2(curX + TimeTextXPadding, curY), new Vector2(0f, 1f), Color.CornflowerBlue * drawAlpha);
            curX += timeTextMaxWidth;
        }

        textRenderer.Draw(messageText, new Vector2(curX, y - MessageYPadding), 1f, drawAlpha);

        if (counterText is not null)
        {
            Vector2 shakeOffset = counterShakeAmplitude > 0f
                ? new Vector2(
                    (Random.Shared.NextSingle() * 2f - 1f) * counterShakeAmplitude,
                    (Random.Shared.NextSingle() * 2f - 1f) * counterShakeAmplitude
                )
                : Vector2.Zero;
            // vertically centered so scaling grows symmetrically
            textRenderer.Draw(
                counterText,
                new Vector2(curX + messageWidth + counterGapWidth, y - messageLineHeight * 0.5f) + shakeOffset,
                new Vector2(0f, 0.5f),
                Vector2.One * counterScale,
                counterColor * drawAlpha
            );
        }

        return;

        static void DrawSnappedRect(float x, float y, float width, float height, Color color)
        {
            float xi = MathF.Floor(x);
            float yi = MathF.Floor(y);
            float wi = MathF.Floor(x + width) - xi;
            float hi = MathF.Floor(y + height) - yi;

            Draw.Rect(xi, yi, wi, hi, color);
        }
    }

    private float MeasureSingleMessage(ChatText chatText, IScalelessTextRenderer textRenderer)
        => chatText.Segments.Aggregate(0f, (v, seg) => v += textRenderer.Measure(seg.Text).X);

    private static string FormatDateTime(DateTime dateTime)
        => dateTime.ToLocalTime().ToString("T", CultureInfo.InvariantCulture);
}
