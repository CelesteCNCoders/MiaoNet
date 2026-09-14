using System;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// virtualized chat message list. only the rows intersecting the viewport are mounted, and the row
// straddling each edge is dimmed by how much of it is visible.
public sealed class ChatMessageListNode : VirtualListNode
{
    private readonly ITextRenderer renderer;
    private readonly ChatListController controller;
    private IChatMessageSource? source;

    public ChatMessageListNode(ITextRenderer renderer, ChatListController controller)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(controller);

        this.renderer = renderer;
        this.controller = controller;

        // no canvas clip: the row straddling the edge is meant to bleed past the clip line
        // instead of being cut off, and the edge fade already hides the overscan rows.
        FadeItemsAtEdges = true;
        OverscanItems = 1;
        DataSource = new Source(this);
    }

    public float MessagePaddingY { get; set; }

    public float LineHeight { get; set; }

    public float Scale { get; set; } = 1f;

    public float BackgroundOpacity { get; set; } = 0.5f;

    public float TextOpacity { get; set; } = 1f;

    // line height plus padding; also the virtual list's item extent.
    public float MessageLineHeight => LineHeight + (2f * MessagePaddingY);

    // number of messages in the displayed list, which the content height comes from.
    //
    // the host reads the count from here and not from the chat snapshot so the virtual list and
    // the scroll clamp can only ever agree: both come from the mounted source. feeding the clamp
    // the full log instead made a tab switch keep the previous tab's content height, so the scroll
    // range and the bottom-anchored offset were both wrong.
    public int MessageCount => source?.Count ?? 0;

    // swaps the displayed messages; mounted nodes are thrown away and rebuilt lazily.
    public void SetMessages(IChatMessageSource? next)
    {
        if (ReferenceEquals(source, next))
        {
            return;
        }

        source = next;
        ItemExtent = MessageLineHeight;
        Reset();

        // push the count here instead of letting the host sync it: the content height has to come
        // from the same list this node renders, and this is the only place that knows it.
        controller.MessageCount = MessageCount;
    }

    // call when the message padding setting changed.
    public void RefreshMetrics()
    {
        float extent = MessageLineHeight;
        if (!ItemExtent.Equals(extent))
        {
            ItemExtent = extent;
            InvalidateMeasure();
        }
    }

    protected override void OnItemMounted(UINode node, int index)
    {
        if (node is not ChatMessageNode message || source is null)
        {
            return;
        }

        message.ApplyMetrics(MessagePaddingY, LineHeight, Scale, BackgroundOpacity, TextOpacity);

        object key = source.GetKey(index);
        message.Fade = controller.FadeOf(key);
        message.CounterPopProgress = controller.CounterPopProgressOf(key);
        message.CounterAnimClock = controller.CounterAnimClock;
    }

    private sealed class Source(ChatMessageListNode owner) : IVirtualListDataSource
    {
        public int Count => owner.source?.Count ?? 0;

        public object GetItemKey(int index)
            => owner.source!.GetKey(index);

        public UINode BuildItem(int index)
            => new ChatMessageNode(owner.renderer, owner.source!.BuildRow(index));
    }
}
