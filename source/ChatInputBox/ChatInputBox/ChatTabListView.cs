namespace Celeste.Mod.ChatInputBox;

public class ChatTabListView
{
    private readonly ChatMessageManager chatMessageManager;
    private readonly IScalelessTextRenderer textRenderer;

    public string InitialTabTitle { get; set; } = string.Empty;

    /// <summary>
    /// Tab index currently focused by the tab-bar cursor (-1 for Global, 0..N-1 for channels),
    /// or null when focus is in the input box.
    /// </summary>
    public int? FocusedTabIndex { get; set; }

    private int activeTabIndex => chatMessageManager.ActiveTabIndex;

    public ChatTabListView(ChatMessageManager chatMessageManager, IScalelessTextRenderer textRenderer)
    {
        this.chatMessageManager = chatMessageManager;
        this.textRenderer = textRenderer;
    }

    public void Render()
    {
        const float Margin = 16f;
        const float Padding = 8f;

        float lineHeight = textRenderer.LineHeight;
        float inputBoxTopY = Engine.Height - Margin - lineHeight - Padding * 2f;
        Vector2 baseLoc = new Vector2(Margin, inputBoxTopY - Padding);
        float curX = baseLoc.X;

        var channels = chatMessageManager.Channels;
        float checkboxSize = MathF.Max(8f, MathF.Floor(lineHeight * 0.52f));
        float checkboxGap = MathF.Max(4f, MathF.Floor(lineHeight * 0.22f));

        for (int i = -1; i < channels.Count; i++)
        {
            bool isChannelTab = i >= 0;
            string title = isChannelTab ? channels[i].Title : InitialTabTitle;
            bool enabledInGlobal = !isChannelTab || channels[i].EnabledInGlobal;

            float textWidth = textRenderer.Measure(title).X;
            float checkboxArea = isChannelTab ? checkboxSize + checkboxGap : 0f;
            float tabWidth = checkboxArea + textWidth + 2f * Padding;
            bool isActive = i == activeTabIndex;
            bool isFocused = FocusedTabIndex == i;

            float tabX = MathF.Floor(curX);
            float tabY = MathF.Floor(baseLoc.Y - lineHeight);
            float tabW = MathF.Floor(tabWidth);

            float bgAlpha = isActive ? 0.55f : (isFocused ? 0.35f : 0.15f);
            Draw.Rect(tabX, tabY, tabW, lineHeight, Color.Black * bgAlpha);

            if (isFocused)
            {
                Draw.HollowRect(tabX, tabY, tabW, lineHeight, Color.Gold);
                Draw.Rect(tabX, tabY - 2f, tabW, 2f, Color.Gold);
            }

            float contentX = curX + Padding;
            float textAlpha = isActive || isFocused ? 1f : (enabledInGlobal ? 0.65f : 0.35f);

            if (isChannelTab)
            {
                float boxX = MathF.Floor(contentX);
                float boxY = MathF.Floor(tabY + (lineHeight - checkboxSize) * 0.5f);
                Color boxColor = (isFocused ? Color.Gold : Color.White) * (enabledInGlobal ? textAlpha : 0.45f);
                Draw.HollowRect(boxX, boxY, checkboxSize, checkboxSize, boxColor);
                if (enabledInGlobal)
                {
                    float inset = MathF.Max(2f, MathF.Floor(checkboxSize * 0.25f));
                    float fillSize = MathF.Max(1f, checkboxSize - inset * 2f);
                    Draw.Rect(boxX + inset, boxY + inset, fillSize, fillSize, boxColor);
                }
                contentX += checkboxArea;
            }

            textRenderer.Draw(
                title,
                new Vector2(contentX, baseLoc.Y),
                new Vector2(0f, 1f),
                Color.White * textAlpha
            );

            curX += tabWidth + 2f;
        }
    }
}
