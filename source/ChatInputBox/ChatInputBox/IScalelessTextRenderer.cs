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
    /// <summary>Lays <paramref name="text"/> out once, for repeated measuring and drawing.</summary>
    public ILiteralTextLayout LayoutLiteral(string text);
}

/// <summary>A raw string laid out once, reusable for measuring and drawing.</summary>
public interface ILiteralTextLayout
{
    public Vector2 Size { get; }

    public int LineCount { get; }

    /// <summary>
    /// Width from the start of <paramref name="start"/>'s line up to <paramref name="end"/>,
    /// which must be on that line.
    /// </summary>
    public float MeasurePrefix(int start, int end);

    /// <summary>
    /// Draws glyphs whose text index is in [<paramref name="start"/>, <paramref name="end"/>),
    /// with <paramref name="position"/> at the line start of <paramref name="start"/>.
    /// </summary>
    public void Draw(
        Vector2 position, int start, int end, float xOffset,
        IReadOnlyList<ColoredTextRange> runs
    );

    public void Draw(Vector2 position, int start, int end, float xOffset, Color color);
}
