using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetTextRenderer : ITextRenderer
{
    private const float Scale = 2f / 3f;

    public float LineHeight => MiaoNetFont.LineHeight * Scale;

    public bool CanRender(int character)
        => MiaoNetFont.FontSize.Characters.ContainsKey(character);

    public Vector2 Measure(string text)
        => MiaoNetFont.FontSize.Measure(text) * Scale;

    public void Draw(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.Font.DrawOutline(
            MiaoNetFont.BaseSize,
            text,
            position,
            justify, Vector2.One * Scale,
            color,
            1f,
            Color.Black with { A = color.A }
        );
}
