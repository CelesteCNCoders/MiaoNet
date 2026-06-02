using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class ChatMessageListView
{
    private record struct ChatItem(ChatText Message, float ShowTimer, float FadeOut = 1f);
    private readonly List<ChatItem> chatLog;
    private readonly ITextRenderer textRenderer;

    private bool active;

    private float lastMouseScrollWheelValue;
    private float targetScroll;
    private float scroll;

    public float BackgroundOpacity { get; set; } = 0.5f;

    public float TextOpacity { get; set; } = 1f;

    public int IdleMaxCount { get; set; } = 12;

    public int ActiveMaxCount { get; set; } = 18;

    public float ShowDuration { get; set; } = 8f;

    public bool NoNewMessagesShowing { get; set; }

    public ChatMessageListView(ITextRenderer textRenderer)
    {
        this.textRenderer = textRenderer;
        chatLog = new();
    }

    public void AddChatMessage(ChatText chatMessage)
    {
        chatLog.Add(new(chatMessage, ShowDuration));
    }

    public void CleanUp()
    {
        chatLog.Clear();
    }

    public float ClampScrollValue(float value)
        => Math.Clamp(value, 0f, Math.Max((chatLog.Count - ActiveMaxCount) * textRenderer.LineHeight, 0));

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

        const float Margin = 16f;
        const float Padding = 8f;

        Vector2 baseLoc = new Vector2(Margin, Engine.Height - Margin - textRenderer.LineHeight * 1.5f - Padding);

        float curY = baseLoc.Y;
        int firstVisibleMessageIndex = chatLog.Count - 1;
        if (active)
        {
            curY += scroll;

            for (int i = chatLog.Count - 1; i >= 0; i--)
            {
                if (curY > baseLoc.Y)
                {
                    curY -= textRenderer.LineHeight;
                    continue;
                }
                firstVisibleMessageIndex = i;
                break;
            }
        }

        if (firstVisibleMessageIndex + 1 < chatLog.Count)
        {
            float pCurY = curY + textRenderer.LineHeight;
            float alpha = 1f - (pCurY - baseLoc.Y) / textRenderer.LineHeight;
            DrawSingleMessage(chatLog[firstVisibleMessageIndex + 1], baseLoc.X, pCurY, alpha);
        }

        int maxCount = active ? ActiveMaxCount : IdleMaxCount;
        int nextInvisibleMessageIndex = -1;
        for (int i = firstVisibleMessageIndex; i >= 0; i--)
        {
            if (curY < baseLoc.Y - maxCount * textRenderer.LineHeight)
            {
                nextInvisibleMessageIndex = i;
                break;
            }

            if (!DrawSingleMessage(chatLog[i], baseLoc.X, curY, 1f))
                break;

            curY -= textRenderer.LineHeight;
        }
        if (nextInvisibleMessageIndex > 0)
        {
            float alpha = 1f - ((baseLoc.Y - maxCount * textRenderer.LineHeight) - curY) / textRenderer.LineHeight;
            DrawSingleMessage(chatLog[nextInvisibleMessageIndex], baseLoc.X, curY, alpha);
        }
    }


    // TODO introducing ChatText.Render?
    private bool DrawSingleMessage(ChatItem item, float x, float curY, float alpha)
    {
        const float Padding = 8f;

        var (msg, _, msgFade) = item;

        float fade = msgFade;
        if (active)
            fade = 1f;
        else if (fade == 0f)
            return false;

        fade *= alpha;

        float lineHeight = textRenderer.LineHeight;
        float lineWidth = MeasureSingleMessage(msg);
        DrawSnappedRect(
            x,
            curY - lineHeight,
            lineWidth + 2 * Padding,
            lineHeight,
            Color.Black * fade * BackgroundOpacity
        );

        float curX = x + Padding;
        foreach (var seg in msg.Segments)
        {
            Vector2 size = textRenderer.Measure(seg.Text);

            if (!seg.Style.HasFlag(ChatTextStyle.Outline))
            {
                textRenderer.Draw(
                    seg.Text,
                    new Vector2(curX, curY),
                    new Vector2(0f, 1f),
                    seg.Color * fade * TextOpacity
                );
            }
            else
            {
                textRenderer.DrawOutline(
                    seg.Text,
                    new Vector2(curX, curY),
                    new Vector2(0f, 1f),
                    seg.Color * fade * TextOpacity
                );
            }

            if (seg.Style.HasFlag(ChatTextStyle.Underscore))
            {
                float thinkness = Math.Max(2f, 4f * textRenderer.LineHeight / 96f);
                Draw.Line(
                    new Vector2(curX, curY),
                    new Vector2(curX + size.X, curY),
                    seg.Color * fade * TextOpacity,
                    thinkness
                );
            }

            if (seg.Style.HasFlag(ChatTextStyle.Strikethrough))
            {
                float thinkness = Math.Max(2f, 4f * textRenderer.LineHeight / 96f);
                Draw.Line(
                    new Vector2(curX, curY - lineHeight / 2f),
                    new Vector2(curX + size.X, curY - lineHeight / 2),
                    seg.Color * fade * TextOpacity,
                    thinkness
                );
            }

            curX += size.X;
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
}