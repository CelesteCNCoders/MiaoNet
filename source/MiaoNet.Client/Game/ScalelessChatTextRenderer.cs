using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

internal sealed class ScalelessChatTextRenderer : ILiteralTextRenderer
{
    public float Scale { get; set; }

    public float LineHeight { get; set; }

    public ScalelessChatTextRenderer(float scale, float lineHeight)
    {
        Scale = scale;
        LineHeight = lineHeight;
    }

    public bool CanRender(int character)
        => MiaoNetFont.CanRender(character);

    public Vector2 Measure(string text)
        => MiaoNetFont.Measure(text) * Scale;

    public Vector2 MeasureLiteral(string text)
        => MiaoNetFont.MeasureLiteral(text) * Scale;

    public void Draw(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.Draw(text, position, justify, Vector2.One * Scale, color);

    public void DrawLiteral(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.DrawLiteral(text, position, justify, Vector2.One * Scale, color);

    public void DrawLiteral(
        string text, int start, int end,
        IReadOnlyList<ChatInputHighlightSpan> runs, Vector2 position
    )
        => MiaoNetFont.DrawLiteral(text, start, end, runs, position, Vector2.One * Scale);

    public void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color)
        => MiaoNetFont.Draw(text, position, justify, Vector2.One * Scale * scale, color);

    public void DrawOutline(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.DrawOutline(
            text, position, justify,
            Vector2.One * Scale, color,
            1f, (IsColorDark(color) ? Color.White : Color.Black)
        );

    public void Draw(ChatText text, Vector2 position, float yJustify, float alpha)
        => MiaoNetFont.Draw(
            text, position, yJustify,
            Vector2.One * Scale,
            alpha
        );

    private static bool IsColorDark(Color color)
    {
        float darkness = 1f - (0.299f * color.R + 0.587f * color.G + 0.114f * color.B) / 255f;
        return darkness > 0.5f;
    }
}
