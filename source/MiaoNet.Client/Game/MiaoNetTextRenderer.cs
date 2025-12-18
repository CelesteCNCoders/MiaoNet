using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetTextRenderer : ITextRenderer
{
    private const float Scale = 2f / 3f;

    public float LineHeight => MiaoNetFont.LineHeight * Scale;

    public bool CanRender(int character)
        => MiaoNetFont.ZhsFontSize.Characters.ContainsKey(character);

    public Vector2 Measure(string text)
        => MiaoNetFont.ZhsFontSize.Measure(text) * Scale;

    public void Draw(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.ZhsFont.DrawOutline(
            MiaoNetFont.ZhsBaseSize,
            text,
            position,
            justify, Vector2.One * Scale,
            color,
            1f,
            Color.Black with { A = color.A }
        );
}
