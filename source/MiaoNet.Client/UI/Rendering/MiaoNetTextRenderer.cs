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

    public void Draw(IUICanvas canvas, string text, Vector2 position, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Color uiColor = style.Color ?? Color.White;
        Vector2 justify = new(style.HorizontalAnchor.HorizontalFactor(), style.VerticalAnchor.VerticalFactor());
        Vector2 vectorScale = new(style.Scale);

        if (style.Decorations.HasFlag(TextDecoration.Outline))
        {
            MiaoNetFont.DrawOutline(text, position, justify, vectorScale, uiColor);
        }
        else
        {
            MiaoNetFont.Draw(text, position, justify, vectorScale, uiColor);
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
            canvas.DrawLine(new Vector2(left, y), new Vector2(right, y), uiColor, thickness);
        }

        if (style.Decorations.HasFlag(TextDecoration.Strikethrough))
        {
            float y = position.Y + (textSize.Y * (1f - style.VerticalAnchor.VerticalFactor())) - (textSize.Y / 2f);
            canvas.DrawLine(new Vector2(left, y), new Vector2(right, y), uiColor, thickness);
        }
    }

    public bool CanRender(int character, TextStyle style)
        => MiaoNetFont.CanRender(character);
}
