using System;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// the chat input box: a background band, the text field, and the completion popup above it.
//
// the popup's bottom edge sits on the box's top edge and is left out of the measured height, so
// showing candidates never pushes the tab strip or the message list. the popup is drawn after the
// field so it overlays the layout.
public sealed class ChatInputNode : MultiChildNode
{
    public ChatInputNode(TextFieldNode field, CompletionPopupNode popup)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(popup);

        Field = field;
        Popup = popup;

        Add(field);
        Add(popup);
    }

    public TextFieldNode Field { get; }

    public CompletionPopupNode Popup { get; }

    public float LineHeight { get; set; }

    public float Scale { get; set; } = 1f;

    public float Padding { get; set; } = ChatLayout.Padding;

    public float BoxHeight => LineHeight + (2f * Padding);

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        Field.LineHeight = LineHeight;
        Field.Scale = Scale;
        Popup.LineHeight = LineHeight;
        Popup.Scale = Scale;

        Field.Measure(constraints.Deflate(new EdgeInsets(Padding)).WithHeight(0f, LineHeight));
        Popup.Measure(BoxConstraints.Unbounded);

        float width = float.IsInfinity(constraints.MaxWidth) ? 0f : constraints.MaxWidth;
        return constraints.Constrain(new UiSize(width, BoxHeight));
    }

    protected override void OnArrange(UiRect bounds)
    {
        var textArea = new UiRect(
            bounds.X + Padding,
            bounds.Y + Padding,
            MathF.Max(0f, bounds.Width - (2f * Padding)),
            LineHeight);
        Field.Arrange(textArea);

        bool showPopup = Field.Controller.Focused && Popup.Items.Count > 0;
        Popup.IsVisible = showPopup;
        if (!showPopup)
        {
            return;
        }

        UiSize size = Popup.MeasuredSize;
        Popup.Arrange(new UiRect(
            bounds.X + Padding + Field.TextBeforeCaretWidth,
            bounds.Y - size.Height,
            size.Width,
            size.Height));
    }

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
        => canvas.FillRect(Bounds, MiaoNetUiTheme.Input.Background * opacity);
}
