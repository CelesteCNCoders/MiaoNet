using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.ChatInputBoxExample;

public sealed class TextRenderer : ITextRenderer
{
    private readonly Language language;

    public float LineHeight => FontSize.LineHeight * Scale;

    private PixelFont Font => Fonts.Get(language.FontFace);

    private float BaseSize => language.FontFaceSize;

    private PixelFontSize FontSize => language.FontSize;

    public float Scale { get; set; }

    public TextRenderer(Language language)
    {
        this.language = language;
    }

    public bool CanRender(int character)
        => FontSize.Characters.ContainsKey(character);

    public Vector2 Measure(string text)
        => FontSize.Measure(text) * Scale;

    public void Draw(string text, Vector2 position, Vector2 justify, Color color)
        => Font.Draw(BaseSize, text, position, justify, Vector2.One * Scale, color);

    public void DrawOutline(string text, Vector2 position, Vector2 justify, Color color)
        => Font.DrawOutline(
            BaseSize,
            text, position, justify,
            Vector2.One * Scale, color,
            1f, color.Invert()
        );
}
