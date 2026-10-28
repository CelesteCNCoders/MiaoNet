using System.Globalization;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class ChatMessageListView
{
    private record struct ChatMessageViewState(float ShowTimer, float FadeOut = 1f, float CounterPopTimer = 0f);
    private readonly Dictionary<ChatItem, ChatMessageViewState> viewStates = new();


    private ChatMessageManager chatMessageManager;
    private readonly IScalelessTextRenderer textRenderer;

    private bool active;

    private float lastMouseScrollWheelValue;
    private float targetScroll;
    private float scroll;

    private List<ChatItem> chatLog => active || NewMessagesShowing == NewMessageShowingMode.WithTab
        ? chatMessageManager.ActiveChatLog
        : NewMessagesShowing == NewMessageShowingMode.ShowAll
            ? fullChatLog
            : [];
    private List<ChatItem> fullChatLog => chatMessageManager.ChatLog;


    public float BackgroundOpacity { get; set; } = 0.5f;

    public float TextOpacity { get; set; } = 1f;

    public float IdleHeight { get; set; } = 0.2f;

    public float ActiveHeight { get; set; } = 0.8f;

    public float ShowDuration { get; set; } = 8f;

    public NewMessageShowingMode NewMessagesShowing { get; set; } = NewMessageShowingMode.ShowAll;

    public bool FancyFoldCounter { get; set; } = true;

    // drives the rainbow color of the fold counter
    private float counterAnimClock;

    public float MessageYPadding { get; set; } = 8f;

    public string? ActiveTabName => chatMessageManager.ActiveTabName;

    // 由宿主提供裁剪作用域，避免组件假设 SpriteBatch 的渲染状态。
    public Action<Rectangle, Action>? RenderClipped { get; set; }

    private ChatMessageViewState getOrInitViewState(ChatItem key)
    {
        if (!viewStates.TryGetValue(key, out var viewState))
        {
            // a freshly folded line appears with its counter pop animation playing
            viewState = key.RepeatCount > 1
                ? new(ShowDuration, CounterPopTimer: FoldCounter.PopDuration)
                : new(ShowDuration);
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
        float totalMessagesHeight = chatLog.Sum(item => item.MeasureHeight(textRenderer, MessageYPadding));

        float lineHeight = textRenderer.LineHeight;
        float baseY = Engine.Height - Margin - (active ? 2 : 1) * (lineHeight + Padding * 2);
        float maxHeightV = (active ? ActiveHeight : IdleHeight) * baseY;

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
        counterAnimClock += Engine.RawDeltaTime;

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
            if (state.CounterPopTimer > 0f)
                state.CounterPopTimer -= Engine.RawDeltaTime;
            if (state.ShowTimer > 0f)
            {
                // NoNewMessage now renders an empty list so no need to manually fade message out anymore.
                state.ShowTimer -= Engine.RawDeltaTime;
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

        // folding replaces the old ChatItem, so sweep its stale view state instead
        // of letting replaced items pile up until the next CleanUp
        if (viewStates.Count > fullChatLog.Count)
        {
            foreach (var stale in viewStates.Keys.Where(k => !fullChatLog.Contains(k)).ToArray())
                viewStates.Remove(stale);
        }
    }

    public void Render()
    {
        const float Margin = 16f;
        const float Padding = 8f;
<<<<<<< Updated upstream

=======
>>>>>>> Stashed changes
        float inputBoxTopY = Engine.Height - Margin - textRenderer.LineHeight - Padding * 2f;
        float tabViewTopY = inputBoxTopY - textRenderer.LineHeight - Padding * 2f;
        float baseY = active ? tabViewTopY : inputBoxTopY;
<<<<<<< Updated upstream
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

=======
>>>>>>> Stashed changes
        float maxHeightV = (active ? ActiveHeight : IdleHeight) * baseY;
        float top = Math.Max(0, baseY - maxHeightV);
        scroll = ClampScrollValue(scroll);

        if (RenderClipped is not null)
            RenderClipped(new Rectangle(0, (int)top, Engine.Width, Math.Max(0, (int)(baseY - top))), DrawMessages);
        else
            DrawMessages();

        void DrawMessages()
        {
            float bottom = baseY + (active ? scroll : 0);
            for (int i = chatLog.Count - 1; i >= 0 && bottom > top; i--)
            {
                var item = chatLog[i];
                float height = item.MeasureHeight(textRenderer, MessageYPadding);
                if (height <= 0)
                    continue;
                float itemTop = bottom - height;
                if (itemTop < baseY)
                {
                    float visible = Math.Min(bottom, baseY) - Math.Max(itemTop, top);
                    float alpha = RenderClipped is null ? Math.Clamp(visible / height, 0, 1) : 1;
                    if (!DrawSingleMessage(item, Margin, bottom, alpha))
                        break;
                }
                bottom = itemTop;
            }
        }
    }

    private bool DrawSingleMessage(ChatItem chatItem, float x, float curY, float alpha)
    {
        var state = getOrInitViewState(chatItem);
        float fade = state.FadeOut;
        if (active)
            fade = 1f;
        else if (fade == 0f)
            return false;
        fade *= alpha;
        chatItem.render(x, curY, fade, BackgroundOpacity, TextOpacity, MessageYPadding, textRenderer,
            FancyFoldCounter, counterAnimClock,
            1f - state.CounterPopTimer / FoldCounter.PopDuration);
        return true;

    }

    public void CleanUp()
    {
        viewStates.Clear();
    }
}
