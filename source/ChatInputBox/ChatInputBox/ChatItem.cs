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
    
    private const float TimeTextWidthRatio = 3.25f;

    public ChatItem(DateTime dateTime, ChatText messageText)
    {
        this.dateTimeText = FormatDateTime(dateTime);
        this.messageText = messageText;
    }

    public ChatItem(ChatText messageText)
    {
        this.messageText = messageText;
    }
    public void render(float x, float y, float fade, float backgroundOpacity, float textOpacity, IScalelessTextRenderer textRenderer)
    {
        float lineHeight = textRenderer.LineHeight;
        float messageLineHeight = lineHeight + 2 * MessageYPadding;
        float timeTextMaxWidth = TimeTextWidthRatio * lineHeight;
        float lineWidth = MeasureSingleMessage(messageText, textRenderer);
        if (dateTimeText is not null)
            lineWidth += timeTextMaxWidth;
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
            textRenderer.Draw(dateTimeText, new Vector2(curX, curY), new Vector2(0f, 1f), Color.CornflowerBlue * drawAlpha);
            curX += timeTextMaxWidth;
        }

        textRenderer.Draw(messageText, new Vector2(curX, y - MessageYPadding), 1f, drawAlpha);

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