namespace Celeste.Mod.ChatInputBox;

public sealed class TextBuffer
{
    public string Text { get; private set; }

    // unfortunately we need this
    public string TextBeforeCaret { get; private set; }

    public string TextAfterCaret { get; private set; }

    public event Action? TextOrCaretChanged;

    public int CaretPosition { get; private set; }

    public TextBuffer(string text = "")
    {
        Text = text;
        TextAfterCaret = "";
        TextBeforeCaret = text;
        CaretPosition = text.Length;
    }

    private bool SetCaretPositionAndClamp(int newPosition)
    {
        int oldPos = CaretPosition;
        CaretPosition = Math.Clamp(newPosition, 0, Text.Length);
        UpdateTextBeforeAfterCaret();
        return oldPos != CaretPosition;
    }

    private void UpdateTextBeforeAfterCaret()
    {
        TextBeforeCaret = Text[..CaretPosition];
        TextAfterCaret = Text[CaretPosition..];
        TextOrCaretChanged?.Invoke();
    }

    public bool MoveCaretToHome()
        => SetCaretPositionAndClamp(0);

    public bool MoveCaretToEnd()
        => SetCaretPositionAndClamp(Text.Length);

    public bool MoveCaretForward()
        => SetCaretPositionAndClamp(CaretPosition + 1);

    public bool MoveCaretForward(int amount)
        => SetCaretPositionAndClamp(CaretPosition + amount);

    public bool MoveCaretBackward()
        => SetCaretPositionAndClamp(CaretPosition - 1);

    public bool MoveCaretBackward(int amount)
        => SetCaretPositionAndClamp(CaretPosition - amount);

    public bool Backspace()
    {
        if (CaretPosition > 0)
        {
            string newText = string.Concat(Text.AsSpan(0, CaretPosition - 1), Text.AsSpan(CaretPosition));
            Text = newText;
            return MoveCaretBackward();
        }
        return false;
    }

    public bool Delete()
    {
        if (CaretPosition < Text.Length)
        {
            string newText = string.Concat(Text.AsSpan(0, CaretPosition), Text.AsSpan(CaretPosition + 1));
            Text = newText;
            UpdateTextBeforeAfterCaret();
            return true;
        }
        return false;
    }

    public void DoCompletion(int remove, string text, char separator, int maxTextLength)
    {
        int start = CaretPosition - remove;
        string afterCaret = TextAfterCaret;

        // Reuse the separator already after the caret instead of inserting a duplicate one.
        bool sameSeparator = afterCaret.Length > 0 && afterCaret[0] == separator;
        bool whitespaceSeparator = afterCaret.Length > 0 &&
            char.IsWhiteSpace(separator) && char.IsWhiteSpace(afterCaret[0]);
        bool separatorExists = sameSeparator || whitespaceSeparator;

        string inserted = separatorExists ? text : text + separator;
        int available = maxTextLength - (Text.Length - remove);
        if (inserted.Length > available)
            inserted = inserted[..Math.Max(0, available)];

        Text = string.Concat(Text.AsSpan(0, start), inserted, afterCaret);
        // Land the caret past the separator, as if the user had typed it.
        SetCaretPositionAndClamp(start + inserted.Length + (separatorExists ? 1 : 0));
    }

    public void Clear()
    {
        Text = string.Empty;
        CaretPosition = 0;
        UpdateTextBeforeAfterCaret();
    }

    public void InputChar(char chr)
    {
        string newText = string.Concat(Text.AsSpan(0, CaretPosition), [chr], Text.AsSpan(CaretPosition));
        Text = newText;
        MoveCaretForward();
    }

    public void InputString(string text)
    {
        string newText = string.Concat(Text.AsSpan(0, CaretPosition), text, Text.AsSpan(CaretPosition));
        Text = newText;
        MoveCaretForward(text.Length);
    }

    public void SetText(string text)
    {
        Text = text;
        MoveCaretToEnd();
    }
}
