namespace Celeste.Mod.ChatInputBox;

public readonly struct ChatInputHighlightSpan
{
    public int Start { get; }

    public int Length { get; }

    public Color Color { get; }

    public ChatInputHighlightSpan(int start, int length, Color color)
    {
        Start = start;
        Length = length;
        Color = color;
    }

    public int End => Start + Length;
}

public interface IChatInputHighlighter
{
    public IReadOnlyList<ChatInputHighlightSpan> Highlight(string text, int caret);
}
