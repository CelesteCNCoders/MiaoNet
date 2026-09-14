using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.Status;

// the connection status overlay in the bottom-left corner: a rotating cogwheel followed by a
// one-line message.
//
// geometry: the cogwheel's box sits CornerOffset from the left edge and from the bottom, and the
// message is bottom-aligned with it, MessageGap to the right.
//
// the children are injected instead of built here so the cogwheel can stay a client-side node: its
// outlined rotation needs Monocle's MTexture internals, which the xna-free core won't expose.
public sealed class StatusPanelNode : MultiChildNode
{
    // distance of the cogwheel's outer edge from the screen's left and bottom edges.
    public const float CornerOffset = 64f;

    // horizontal gap between the cogwheel and the message.
    public const float MessageGap = 32f;

    private UiSize iconSize;
    private UiSize messageSize;

    public StatusPanelNode(UiNode icon, UiNode message)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(message);

        Icon = icon;
        Message = message;
        Add(icon);
        Add(message);
    }

    public UiNode Icon { get; }

    public UiNode Message { get; }

    // the area the overlay is laid out in; normally the whole screen.
    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        // children size themselves; the panel just fills the screen it's given.
        iconSize = Icon.Measure(BoxConstraints.Unbounded);
        messageSize = Message.Measure(BoxConstraints.Unbounded);

        float width = float.IsInfinity(constraints.MaxWidth) ? 0f : constraints.MaxWidth;
        float height = float.IsInfinity(constraints.MaxHeight) ? 0f : constraints.MaxHeight;
        return constraints.Constrain(new UiSize(width, height));
    }

    protected override void OnArrange(UiRect bounds)
    {
        float iconX = CornerOffset;
        float iconY = bounds.Height - CornerOffset - iconSize.Height;
        Icon.Arrange(new UiRect(iconX, iconY, iconSize.Width, iconSize.Height));

        // message bottom lines up with the cogwheel's, i.e. CornerOffset above the screen bottom.
        float messageX = iconX + iconSize.Width + MessageGap;
        float messageY = bounds.Height - CornerOffset - messageSize.Height;
        Message.Arrange(new UiRect(messageX, messageY, messageSize.Width, messageSize.Height));
    }
}
