using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// renders a TextEditingController's editing state: text before the caret, the ime composition,
// the text after it, and the caret itself.
//
// the caret sits at the start of the ime composition, not the end.
public sealed class TextFieldNode : UiNode
{
    private readonly ITextRenderer renderer;

    public TextFieldNode(ITextRenderer renderer, TextEditingController controller)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(controller);

        this.renderer = renderer;
        Controller = controller;
    }

    public TextEditingController Controller { get; }

    public float LineHeight { get; set; }

    public float Scale { get; set; } = 1f;

    // CHAT.INPUT.CARET_W
    public float CaretWidth { get; set; } = 2f;

    // width of the text before the caret; the completion popup anchors here
    public float TextBeforeCaretWidth => renderer.Measure(Controller.TextBeforeCaret, TextStyle()).Width;

    // caret x, including the consumed ime prefix
    public float CaretX
    {
        get
        {
            float x = Bounds.X + TextBeforeCaretWidth;
            if (Controller.ImeText is { } ime)
            {
                string prefix = ime[..Math.Clamp(Controller.ImeStart, 0, ime.Length)];
                x += renderer.Measure(prefix, TextStyle()).Width;
            }
            return x;
        }
    }

    protected override UiSize OnMeasure(BoxConstraints constraints)
        => constraints.Constrain(new UiSize(constraints.MaxWidth, LineHeight));

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
    {
        TextStyle style = TextStyle() with { Color = MiaoNetUiTheme.Input.Text * opacity };
        float baseline = Bounds.Bottom;
        float x = Bounds.X;

        renderer.Draw(canvas, Controller.TextBeforeCaret, new UiOffset(x, baseline), style);
        x += TextBeforeCaretWidth;

        if (Controller.ImeText is { Length: > 0 } ime)
        {
            TextStyle imeStyle = style with { Color = MiaoNetUiTheme.Input.ImeText * opacity };
            renderer.Draw(canvas, ime, new UiOffset(x, baseline), imeStyle);
            x += renderer.Measure(ime, imeStyle).Width;
        }

        renderer.Draw(canvas, Controller.TextAfterCaret, new UiOffset(x, baseline), style);

        if (Controller.ShowCaret)
        {
            float caretX = CaretX;
            canvas.DrawLine(
                new UiOffset(caretX, Bounds.Bottom),
                new UiOffset(caretX, Bounds.Bottom - LineHeight),
                MiaoNetUiTheme.Input.Caret * opacity,
                CaretWidth);
        }
    }

    private TextStyle TextStyle() => new()
    {
        Scale = Scale,
        LineHeight = LineHeight,
        Color = MiaoNetUiTheme.Input.Text,
        HorizontalAnchor = HorizontalAnchor.Left,
        VerticalAnchor = VerticalAnchor.Bottom,
    };
}
