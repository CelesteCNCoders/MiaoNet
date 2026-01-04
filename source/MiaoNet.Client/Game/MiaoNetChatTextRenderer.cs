using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetChatTextRenderer : ITextRenderer
{
    public float Scale { get; set; }

    public float LineHeight { get; set; }

    public MiaoNetChatTextRenderer(float scale, float lineHeight)
    {
        Scale = scale;
        LineHeight = lineHeight;
    }

    public bool CanRender(int character)
        => MiaoNetFont.CanRender(character);

    public Vector2 Measure(string text)
        => MiaoNetFont.Measure(text) * Scale;

    public void Draw(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.Draw(text, position, justify, Vector2.One * Scale, color);

    public void DrawOutline(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.DrawOutline(
            text, position, justify,
            Vector2.One * Scale, color,
            1f, (IsColorDark(color) ? Color.White : Color.Black) * (color.A / 255f) * (color.A / 255f)
        );

    private static bool IsColorDark(Color color)
    {
        float darkness = 1f - (0.299f * color.R + 0.587f * color.G + 0.114f * color.B) / 255f;
        return darkness > 0.5f;
    }
}
