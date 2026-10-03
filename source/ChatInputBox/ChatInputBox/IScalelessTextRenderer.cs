namespace Celeste.Mod.ChatInputBox;

public interface IScalelessTextRenderer
{
    public float LineHeight { get; }

    // the base scale applied to all text
    public float Scale { get; }

    public bool CanRender(int character);

    public Vector2 Measure(string text);

    public void Draw(string text, Vector2 position, Vector2 justify, Color color);

    // draws with an extra scale multiplier on top of Scale
    public void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color);

    public void DrawOutline(string text, Vector2 position, Vector2 justify, Color color);

    public void Draw(ChatText text, Vector2 position, float yJustify, float alpha);
}

// Draws a raw string with per-glyph colors, which the ChatText-based API above cannot express.
public interface ILiteralTextRenderer : IScalelessTextRenderer
{
    public Vector2 MeasureLiteral(string text);

    public void DrawLiteral(string text, Vector2 position, Vector2 justify, Color color);

    public void DrawLiteral(
        string text, int start, int end,
        IReadOnlyList<ChatInputHighlightSpan> runs, Vector2 position
    );
}
