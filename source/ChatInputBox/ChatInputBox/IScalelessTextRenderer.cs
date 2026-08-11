namespace Celeste.Mod.ChatInputBox;

public interface IScalelessTextRenderer
{
    public float LineHeight { get; }

    /// <summary>
    /// The base scale the renderer applies to all text.
    /// </summary>
    public float Scale { get; }

    public bool CanRender(int character);

    public Vector2 Measure(string text);

    public void Draw(string text, Vector2 position, Vector2 justify, Color color);

    /// <summary>
    /// Draws text with an extra scale multiplier applied on top of <see cref="Scale"/>.
    /// </summary>
    public void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color);

    public void DrawOutline(string text, Vector2 position, Vector2 justify, Color color);

    public void Draw(ChatText text, Vector2 position, float yJustify, float alpha);
}