namespace Celeste.Mod.ChatInputBox;

public sealed class TextBuffer
{
    public string Text { get; private set; }

    public int CaretPosition { get; private set; }

    public TextBuffer(string text = "")
    {
        Text = text;
        CaretPosition = text.Length;
    }

    public static implicit operator TextBuffer(string text)
        => new(text);

    private bool SetCaretPositionAndClamp(int newPosition)
    {
        int oldPos = CaretPosition;
        CaretPosition = newPosition;
        ClampCaretPosition();
        return oldPos != CaretPosition;
    }

    public bool BackwardToHomeCaret()
        => SetCaretPositionAndClamp(0);

    public bool ForwardToEndCaret()
        => SetCaretPositionAndClamp(Text.Length);

    public bool ForwardCaret()
        => SetCaretPositionAndClamp(CaretPosition + 1);

    public bool ForwardCaret(int amount)
        => SetCaretPositionAndClamp(CaretPosition + amount);

    public bool BackwardCaret()
        => SetCaretPositionAndClamp(CaretPosition - 1);

    public bool BackwardCaret(int amount)
        => SetCaretPositionAndClamp(CaretPosition - amount);

    public bool Backspace()
    {
        if (CaretPosition > 0)
        {
            string newText = string.Concat(Text.AsSpan(0, CaretPosition - 1), Text.AsSpan(CaretPosition));
            Text = newText;
            return BackwardCaret();
        }
        return false;
    }

    public bool Delete()
    {
        if (CaretPosition < Text.Length)
        {
            string newText = string.Concat(Text.AsSpan(0, CaretPosition), Text.AsSpan(CaretPosition + 1));
            Text = newText;
            return true;
        }
        return false;
    }

    public void Clear()
    {
        Text = string.Empty;
        CaretPosition = 0;
    }

    public void InputChar(char chr)
    {
        string newText = string.Concat(Text.AsSpan(0, CaretPosition), [chr], Text.AsSpan(CaretPosition));
        Text = newText;
        ForwardCaret();
    }

    public void InputString(string text)
    {
        string newText = string.Concat(Text.AsSpan(0, CaretPosition), text, Text.AsSpan(CaretPosition));
        Text = newText;
        ForwardCaret(text.Length);
    }

    public void SetText(string text)
    {
        Text = text;
        ForwardToEndCaret();
    }

    private void ClampCaretPosition()
        => CaretPosition = Math.Clamp(CaretPosition, 0, Text.Length);
}