using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// screen layout of the chat: the message list on top, the tab strip under it, and the input box in
// the bottom band.
//
// this is the only place that encodes the stacked anchors: everything hangs off the
// input box's top edge, which sits a margin + line height + padding above the bottom.
public sealed class ChatScreenNode : MultiChildNode
{
    public ChatScreenNode(ChatMessageListNode messages, ChatTabBarNode tabs, ChatInputNode input)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(input);

        Messages = messages;
        Tabs = tabs;
        Input = input;

        // draw order: messages, then the tab strip, then the input box on top.
        Add(messages);
        Add(tabs);
        Add(input);

        // only shown while the input box is open.
        tabs.IsVisible = false;
        input.IsVisible = false;
    }

    public ChatMessageListNode Messages { get; }

    public ChatTabBarNode Tabs { get; }

    public ChatInputNode Input { get; }

    private bool active;

    // whether the input box is open. shows the tab strip and the input box, and lifts the message
    // baseline above them.
    public bool Active
    {
        get => active;
        set
        {
            if (active == value)
            {
                return;
            }

            active = value;
            Tabs.IsVisible = value;
            Input.IsVisible = value;
            InvalidateMeasure();
        }
    }

    public float LineHeight { get; set; }

    // fraction of the baseline height the list gets when idle.
    public float IdleRatio { get; set; } = 0.4f;

    // fraction of the baseline height the list gets while the input box is open.
    public float ActiveRatio { get; set; } = 0.8f;

    public float InputTopY(float height)
        => height - ChatLayout.Margin - LineHeight - (2f * ChatLayout.Padding);

    // bottom edge of the tab strip; sits Padding above the input box.
    public float TabsBottomY(float height) => InputTopY(height) - ChatLayout.Padding;

    public float TabsTopY(float height) => TabsBottomY(height) - LineHeight;

    public float BaseY(float height)
        => Active ? TabsTopY(height) - ChatLayout.Padding : InputTopY(height);

    // floored to a whole number of message rows.
    public float ListHeight(float height)
    {
        float rowHeight = Messages.MessageLineHeight;
        if (rowHeight <= 0f)
        {
            return 0f;
        }

        float available = (Active ? ActiveRatio : IdleRatio) * BaseY(height);
        return MathF.Floor(available / rowHeight) * rowHeight;
    }

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        float width = float.IsInfinity(constraints.MaxWidth) ? 0f : constraints.MaxWidth;
        float height = float.IsInfinity(constraints.MaxHeight) ? 0f : constraints.MaxHeight;

        // children are measured here so the message list learns its content height, which the
        // scroll controller needs; OnArrange only assigns rects.
        float contentWidth = MathF.Max(0f, width - (2f * ChatLayout.Margin));
        Messages.Measure(new BoxConstraints(contentWidth, contentWidth, 0f, ListHeight(height)));
        Tabs.Measure(new BoxConstraints(0f, contentWidth, LineHeight, LineHeight));
        Input.Measure(new BoxConstraints(contentWidth, contentWidth, Input.BoxHeight, Input.BoxHeight));

        return constraints.Constrain(new UiSize(width, height));
    }

    protected override void OnArrange(UiRect bounds)
    {
        float contentWidth = MathF.Max(0f, bounds.Width - (2f * ChatLayout.Margin));
        float listHeight = ListHeight(bounds.Height);

        Messages.Arrange(new UiRect(
            ChatLayout.Margin,
            BaseY(bounds.Height) - listHeight,
            contentWidth,
            listHeight));

        // always arranged so the geometry stays valid while hidden.
        Tabs.Arrange(new UiRect(
            ChatLayout.Margin,
            TabsTopY(bounds.Height),
            contentWidth,
            LineHeight));

        // the input box occupies the bottom band. always laid out so the caret and popup keep
        // their positions even while the field has no focus.
        Input.Arrange(new UiRect(
            ChatLayout.Margin,
            InputTopY(bounds.Height),
            contentWidth,
            Input.BoxHeight));
    }
}
