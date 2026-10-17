namespace Celeste.Mod.ChatInputBox;

public readonly struct ColoredTextRange
{
    public int Start { get; }

    public int Length { get; }

    public Color Color { get; }

    public ColoredTextRange(int start, int length, Color color)
    {
        Start = start;
        Length = length;
        Color = color;
    }

    public int End => Start + Length;
}

public interface IChatInputHighlighter
{
    public IReadOnlyList<ColoredTextRange> Highlight(string text, int caret);
}
