using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// one line of text. the node rect is its layout box, and the text is drawn at TextStyle's
// anchor inside it, so callers position text by arranging the node instead of computing coords.
public sealed class TextNode : UINode
{
    private string text = string.Empty;
    private TextStyle textStyle = TextStyle.Default;

    public string Text
    {
        get => text;
        set
        {
            string next = value ?? string.Empty;
            if (text == next)
            {
                return;
            }

            text = next;
            InvalidateMeasure();
        }
    }

    public TextStyle TextStyle
    {
        get => textStyle;
        set
        {
            textStyle = value ?? TextStyle.Default;
            InvalidateMeasure();
        }
    }

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        ITextRenderer? renderer = Style.TextRenderer;
        if (renderer is null || text.Length == 0)
        {
            return constraints.Constrain(new UISize(0f, textStyle.LineHeight ?? 0f));
        }

        return constraints.Constrain(renderer.Measure(text, ResolveStyle()));
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        ITextRenderer? renderer = Style.TextRenderer;
        if (renderer is null || text.Length == 0)
        {
            return;
        }

        TextStyle style = ResolveStyle();
        UIColor color = style.Color ?? Style.Foreground ?? UIColor.White;
        style = style with { Color = color * opacity };

        float x = style.HorizontalAnchor switch
        {
            HorizontalAnchor.Left => Bounds.X,
            HorizontalAnchor.Center => Bounds.X + (Bounds.Width * 0.5f),
            _ => Bounds.Right,
        };
        float y = style.VerticalAnchor switch
        {
            VerticalAnchor.Top => Bounds.Y,
            VerticalAnchor.Center => Bounds.Y + (Bounds.Height * 0.5f),
            _ => Bounds.Bottom,
        };

        renderer.Draw(canvas, text, new UIOffset(x, y), style);
    }

    private TextStyle ResolveStyle() => textStyle with
    {
        LineHeight = textStyle.LineHeight ?? Style.LineHeight,
    };
}
