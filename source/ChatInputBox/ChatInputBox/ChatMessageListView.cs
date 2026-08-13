using System.Globalization;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class ChatMessageListView
{
    private record struct ChatMessageViewState(float ShowTimer, float FadeOut = 1f);
    private readonly Dictionary<ChatText, ChatMessageViewState> viewStates = new();


    private ChatMessageManager chatMessageManager;
    private readonly IScalelessTextRenderer textRenderer;

    private bool active;

    private float lastMouseScrollWheelValue;
    private float targetScroll;
    private float scroll;

    private List<ChatText> chatLog => chatMessageManager.ActiveChatLog;
    private List<ChatText> fullChatLog => chatMessageManager.ChatLog;

    
    public float BackgroundOpacity { get; set; } = 0.5f;

    public float TextOpacity { get; set; } = 1f;

    public float IdleHeight { get; set; } = 0.2f;

    public float ActiveHeight { get; set; } = 0.8f;

    public float ShowDuration { get; set; } = 8f;

    public bool NoNewMessagesShowing { get; set; }

    public string? ActiveTabName => chatMessageManager.ActiveTabName;

    private ChatMessageViewState getOrInitViewState(ChatText key)
    {
        if (!viewStates.TryGetValue(key, out var viewState))
        {
            viewState = new(ShowDuration);
        }
        return viewState;
    }
    

    public ChatMessageListView(ChatMessageManager chatMessageManager, IScalelessTextRenderer textRenderer)
    {
        this.chatMessageManager = chatMessageManager;
        this.textRenderer = textRenderer;
    }

    private float ClampScrollValue(float value)
    {
        // can we avoid recalculating these?
        const float Margin = 16f;
        const float Padding = 8f;
        const float MessageYPadding = 8f;

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
        // this seems a fna bug...
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

        for (int i = fullChatLog.Count - 1; i >= 0; i--)
        {
            var item = fullChatLog[i];
            var state = getOrInitViewState(item);
            if (state.ShowTimer > 0f)
            {
                if (NoNewMessagesShowing)
                {
                    state.ShowTimer = 0f;
                    state.FadeOut = 0f;
                }
                else
                {
                    state.ShowTimer -= Engine.RawDeltaTime;
                }
            }
            else
            {
                if (state.FadeOut > 0f)
                {
                    const float DisappearDuration = 0.25f;
                    state.FadeOut -= (1f / DisappearDuration) * Engine.RawDeltaTime;
                    if (state.FadeOut < 0f)
                        state.FadeOut = 0f;
                }
                else
                {
                    break;
                }
            }
            viewStates[item] = state;
        }
    }

    public void Render()
    {
        // if (showingChatLog.Count == 0)
        //     return;

        const float Margin = 16f;
        const float Padding = 8f;
        const float MessageXPadding = 8f;
        const float MessageYPadding = 8f;
        
        float inputBoxTopY = Engine.Height - Margin - textRenderer.LineHeight - Padding * 2f;
        float tabViewTopY = inputBoxTopY - textRenderer.LineHeight - Padding * 2f;

        float lineHeight = textRenderer.LineHeight;
        float messageLineHeight = lineHeight + 2 * MessageYPadding;

        float baseY = tabViewTopY;
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

    private bool DrawSingleMessage(ChatText msg, float x, float curY, float alpha)
    {
        const float MessageXPadding = 8f;
        const float MessageYPadding = 8f;

        float fade = getOrInitViewState(msg).FadeOut;
        if (active)
            fade = 1f;
        else if (fade == 0f)
            return false;

        fade *= alpha;

        float lineHeight = textRenderer.LineHeight;
        float messageLineHeight = lineHeight + 2 * MessageYPadding;
        float lineWidth = MeasureSingleMessage(msg);
        DrawSnappedRect(
            x,
            curY - messageLineHeight,
            lineWidth + 2 * MessageXPadding,
            messageLineHeight,
            Color.Black * fade * BackgroundOpacity
        );

        float drawAlpha = fade * TextOpacity;

        float curX = x + MessageXPadding;

        textRenderer.Draw(msg, new Vector2(curX, curY - MessageYPadding), 1f, drawAlpha);

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