using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// ITextRenderer over MiaoNet's merged pixel font. anchoring maps the text style to justify
// vectors, and decoration geometry follows MiaoNetFont.Draw(ChatText, ...) so underline and
// strikethrough land on the same pixels.
public sealed class MiaoNetTextRenderer : ITextRenderer
{
    public static MiaoNetTextRenderer Instance { get; } = new();

    public UISize Measure(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return UISize.Zero;
        }

        Vector2 size = MiaoNetFont.Measure(text) * style.Scale;
        return new UISize(size.X, size.Y);
    }

    public void Draw(IUICanvas canvas, string text, UIOffset position, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        UIColor uiColor = style.Color ?? UIColor.White;
        Color color = uiColor.ToXna();
        Vector2 justify = style.HorizontalAnchor.ToJustify(style.VerticalAnchor);
        Vector2 vectorScale = new(style.Scale);

        if (style.Decorations.HasFlag(TextDecoration.Outline))
        {
            MiaoNetFont.DrawOutline(text, position.ToVector2(), justify, vectorScale, color);
        }
        else
        {
            MiaoNetFont.Draw(text, position.ToVector2(), justify, vectorScale, color);
        }

        if ((style.Decorations & (TextDecoration.Underline | TextDecoration.Strikethrough)) == 0)
        {
            return;
        }

        Vector2 textSize = MiaoNetFont.Measure(text) * style.Scale;
        float left = position.X - (textSize.X * style.HorizontalAnchor.HorizontalFactor());
        float right = left + textSize.X;
        float lineHeight = style.LineHeight ?? (MiaoNetFont.ENZhsLineHeight * style.Scale);
        float thickness = MathF.Max(2f, style.Scale * 4f * lineHeight / 96f);

        if (style.Decorations.HasFlag(TextDecoration.Underline))
        {
            float y = position.Y + (textSize.Y * (1f - style.VerticalAnchor.VerticalFactor()));
            canvas.DrawLine(new UIOffset(left, y), new UIOffset(right, y), uiColor, thickness);
        }

        if (style.Decorations.HasFlag(TextDecoration.Strikethrough))
        {
            float y = position.Y + (textSize.Y * (1f - style.VerticalAnchor.VerticalFactor())) - (textSize.Y / 2f);
            canvas.DrawLine(new UIOffset(left, y), new UIOffset(right, y), uiColor, thickness);
        }
    }

    public bool CanRender(int character, TextStyle style)
        => MiaoNetFont.CanRender(character);
}
