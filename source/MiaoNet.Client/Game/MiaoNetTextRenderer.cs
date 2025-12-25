using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetTextRenderer : ITextRenderer
{
    private static float Scale => MiaoNetModule.Settings.UIScaleValue;

    public float LineHeight => MiaoNetFont.ENZhsLineHeight * Scale;

    public bool CanRender(int character)
        => MiaoNetFont.CanRender(character);

    public Vector2 Measure(string text)
        => MiaoNetFont.Measure(text) * Scale;

    public void Draw(string text, Vector2 position, Vector2 justify, Color color)
        => MiaoNetFont.Draw(text, position, justify, Vector2.One * Scale, color);
}
