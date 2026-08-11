using System.Globalization;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class ChatMessageListView
{
    private record struct ChatItem(
        string? DateTimeText,
        ChatText Message,
        float ShowTimer,
        float FadeOut = 1f,
        int RepeatCount = 1,
        float MergePopTimer = ChatTextMerge.MergePopDuration,
        float LastEventTime = 0f
    );

    private const float Margin = 16f;
    private const float Padding = 8f;
    private const float MessageXPadding = 8f;
    private const float MessageYPadding = 8f;

    // small left gap between the message text and the "X{n}" repeat counter
    private const float RepeatCounterGap = 4f;

    // hm, magic number
    private const float TimeTextWidthRatio = 3.25f;

    private readonly List<ChatItem> chatLog;
    private readonly IScalelessTextRenderer textRenderer;

    private bool active;

    // view-local clock (seconds), used for the spam merge window
    private float viewClock;

    private float lastMouseScrollWheelValue;
    private float targetScroll;
    private float scroll;

    public float BackgroundOpacity { get; set; } = 0.5f;

    public float TextOpacity { get; set; } = 1f;

    public float IdleHeight { get; set; } = 0.2f;

    public float ActiveHeight { get; set; } = 0.8f;

    public float ShowDuration { get; set; } = 8f;

    public bool NoNewMessagesShowing { get; set; }

    public ChatMessageListView(IScalelessTextRenderer textRenderer)
    {
        this.textRenderer = textRenderer;
        chatLog = new();
    }

    public void AddChatMessage(DateTime dateTime, ChatText chatMessage)
    {
        if (TryMergeIntoLast(chatMessage))
            return;
        chatLog.Add(new(FormatDateTime(dateTime), chatMessage, ShowDuration, LastEventTime: viewClock));
    }

    public void AddChatMessage(ChatText chatMessage)
    {
        if (TryMergeIntoLast(chatMessage))
            return;
        chatLog.Add(new(null, chatMessage, ShowDuration, LastEventTime: viewClock));
    }

    // spam merge: the same content sent again within the merge window
    // gets merged into the last line instead of appending a duplicate
    private bool TryMergeIntoLast(ChatText chatMessage)
    {
        if (chatLog.Count == 0)
            return false;
        var last = chatLog[^1];
        if (!ChatTextMerge.ShouldMerge(viewClock - last.LastEventTime)
            || !ChatTextMerge.ContentEquals(last.Message, chatMessage))
            return false;

        last.RepeatCount++;
        last.ShowTimer = ShowDuration;
        last.FadeOut = 1f;
        last.MergePopTimer = 0f;
        last.LastEventTime = viewClock;
        chatLog[^1] = last;
        return true;
    }

    public void CleanUp()
    {
        chatLog.Clear();
    }

    private float ClampScrollValue(float value)
    {
        // can we avoid recalculating these?
        float messageLineHeight = (textRenderer.LineHeight + 2 * MessageYPadding);
        float totalMessagesHeight = chatLog.Count * messageLineHeight;

        float lineHeight = textRenderer.LineHeight;
        float baseY = Engine.Height - Margin - lineHeight * 1.5f - Padding;
        float maxHeightV = (active ? ActiveHeight : IdleHeight) * baseY;
        maxHeightV = (int)(maxHeightV / messageLineHeight) * messageLineHeight;

        return Math.Clamp(value, 0f, Math.Max(totalMessagesHeight - maxHeightV, 0f));
    }

    public void Activate()
    {
        active = true;
    }

    public void Deactivate()
    {
        active = false;
        targetScroll = 0f;
        scroll = 0f;
    }

    public void Update()
    {
        viewClock += Engine.RawDeltaTime;

        // this seems an fna bug...
        // we need to manually call `MouseState.Get()`
        float currentScrollWheelValue = Mouse.GetState().ScrollWheelValue;
        float scrollDelta = currentScrollWheelValue - lastMouseScrollWheelValue;
        lastMouseScrollWheelValue = currentScrollWheelValue;

        const float KeyboardScrollSpeed = 1024f;
        if (MInput.Keyboard.Check(Keys.PageUp))
            scrollDelta += KeyboardScrollSpeed * Engine.RawDeltaTime;
        else if (MInput.Keyboard.Check(Keys.PageDown))
            scrollDelta -= KeyboardScrollSpeed * Engine.RawDeltaTime;

        targetScroll += scrollDelta;
        targetScroll = ClampScrollValue(targetScroll);
        float maxMove = Math.Max(Math.Abs(targetScroll - scroll), 8f) * 8f * Engine.RawDeltaTime;
        scroll = Calc.Approach(scroll, targetScroll, maxMove);

        for (int i = chatLog.Count - 1; i >= 0; i--)
        {
            var item = chatLog[i];
            if (item.MergePopTimer < ChatTextMerge.MergePopDuration)
                item.MergePopTimer += Engine.RawDeltaTime;
            if (item.ShowTimer > 0f)
            {
                if (NoNewMessagesShowing)
                {
                    item.ShowTimer = 0f;
                    item.FadeOut = 0f;
                }
                else
                {
                    item.ShowTimer -= Engine.RawDeltaTime;
                }
            }
            else
            {
                if (item.FadeOut > 0f)
                {
                    const float DisappearDuration = 0.25f;
                    item.FadeOut -= (1f / DisappearDuration) * Engine.RawDeltaTime;
                    if (item.FadeOut < 0f)
                        item.FadeOut = 0f;
                }
                else
                {
                    break;
                }
            }
            chatLog[i] = item;
        }
    }

    public void Render()
    {
        if (chatLog.Count == 0)
            return;

        float lineHeight = textRenderer.LineHeight;
        float messageLineHeight = lineHeight + 2 * MessageYPadding;

        float baseY = Engine.Height - Margin - lineHeight * 1.5f - Padding;
        Vector2 baseLoc = new Vector2(Margin, baseY);

        float curY = baseLoc.Y;
        int firstVisibleMessageIndex = chatLog.Count - 1;
        if (active)
        {
            curY += scroll;

            for (int i = chatLog.Count - 1; i >= 0; i--)
            {
                if (curY > baseLoc.Y)
                {
                    curY -= messageLineHeight;
                    continue;
                }
                firstVisibleMessageIndex = i;
                break;
            }
        }

        if (firstVisibleMessageIndex + 1 < chatLog.Count)
        {
            float pCurY = curY + messageLineHeight;
            float alpha = 1f - (pCurY - baseLoc.Y) / messageLineHeight;
            DrawSingleMessage(chatLog[firstVisibleMessageIndex + 1], baseLoc.X, pCurY, alpha);
        }

        float maxHeightV = (active ? ActiveHeight : IdleHeight) * baseY;
        maxHeightV = (int)(maxHeightV / messageLineHeight) * messageLineHeight;
        int nextInvisibleMessageIndex = -1;
        for (int i = firstVisibleMessageIndex; i >= 0; i--)
        {
            if (curY < baseLoc.Y - maxHeightV)
            {
                nextInvisibleMessageIndex = i;
                break;
            }

            if (!DrawSingleMessage(chatLog[i], baseLoc.X, curY, 1f))
                break;

            curY -= messageLineHeight;
        }
        if (nextInvisibleMessageIndex > 0)
        {
            float alpha = 1f - (baseLoc.Y - maxHeightV - curY) / messageLineHeight;
            DrawSingleMessage(chatLog[nextInvisibleMessageIndex], baseLoc.X, curY, alpha);
        }
    }

    private bool DrawSingleMessage(ChatItem item, float x, float y, float alpha)
    {
        string? dateTimeText = item.DateTimeText;
        ChatText msg = item.Message;

        float fade = item.FadeOut;
        if (active)
            fade = 1f;
        else if (fade == 0f)
            return false;

        fade *= alpha;

        float lineHeight = textRenderer.LineHeight;
        float messageLineHeight = lineHeight + 2 * MessageYPadding;
        float timeTextMaxWidth = TimeTextWidthRatio * lineHeight;
        float messageWidth = MeasureSingleMessage(msg);
        float lineWidth = messageWidth;
        if (dateTimeText is not null)
            lineWidth += timeTextMaxWidth;

        // "X{n}" repeat counter for merged spam messages
        string? repeatCounterText = null;
        float repeatCounterScale = 1f;
        float repeatCounterGapWidth = 0f;
        if (item.RepeatCount > 1)
        {
            repeatCounterText = $"X{item.RepeatCount}";
            float popProgress = Math.Min(item.MergePopTimer / ChatTextMerge.MergePopDuration, 1f);
            repeatCounterScale = ChatTextMerge.GetCounterScale(item.RepeatCount)
                + ChatTextMerge.GetCounterPopScale(Ease.ElasticOut(popProgress));
            repeatCounterGapWidth = RepeatCounterGap * textRenderer.Scale;
            // measure at the current (animated) scale so the background always covers the counter
            lineWidth += repeatCounterGapWidth + textRenderer.Measure(repeatCounterText).X * repeatCounterScale;
        }

        DrawSnappedRect(
            x,
            y - messageLineHeight,
            lineWidth + 2 * MessageXPadding,
            messageLineHeight,
            Color.Black * fade * BackgroundOpacity
        );

        float drawAlpha = fade * TextOpacity;

        float curX = x + MessageXPadding;
        float curY = y - MessageYPadding;

        if (dateTimeText is not null)
        {
            textRenderer.Draw(dateTimeText, new Vector2(curX, curY), new Vector2(0f, 1f), Color.CornflowerBlue * drawAlpha);
            curX += timeTextMaxWidth;
        }

        textRenderer.Draw(msg, new Vector2(curX, curY), 1f, drawAlpha);

        if (repeatCounterText is not null)
        {
            // only the counter shakes/scales; the message text stays static
            float shakeAmplitude = ChatTextMerge.GetCounterShakeAmplitude(item.RepeatCount) * textRenderer.Scale;
            Vector2 shakeOffset = shakeAmplitude > 0f
                ? new Vector2(
                    Calc.Random.Range(-shakeAmplitude, shakeAmplitude),
                    Calc.Random.Range(-shakeAmplitude, shakeAmplitude)
                )
                : Vector2.Zero;
            Color counterColor = Color.Lerp(
                Color.White,
                Color.Red,
                ChatTextMerge.GetCounterColorLerp(item.RepeatCount)
            );
            textRenderer.Draw(
                repeatCounterText,
                new Vector2(curX + messageWidth + repeatCounterGapWidth, curY) + shakeOffset,
                new Vector2(0f, 1f),
                Vector2.One * repeatCounterScale,
                counterColor * drawAlpha
            );
        }

        return true;

        static void DrawSnappedRect(float x, float y, float width, float height, Color color)
        {
            float xi = MathF.Floor(x);
            float yi = MathF.Floor(y);
            float wi = MathF.Floor(x + width) - xi;
            float hi = MathF.Floor(y + height) - yi;

            Draw.Rect(xi, yi, wi, hi, color);
        }
    }

    private float MeasureSingleMessage(ChatText chatText)
        => chatText.Segments.Aggregate(0f, (v, seg) => v += textRenderer.Measure(seg.Text).X);

    private static string FormatDateTime(DateTime dateTime)
        => dateTime.ToLocalTime().ToString("T", CultureInfo.InvariantCulture);
}