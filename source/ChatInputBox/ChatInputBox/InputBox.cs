using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class InputBox
{
    public const float CaretBlinkInterval = 0.5f;

    private readonly ILiteralTextRenderer textRenderer;
    private readonly ICompletionProvider completionProvider;
    private readonly IChatInputHighlighter? highlighter;

    private readonly TextBuffer buffer;
    private List<Completion>? completions;

    private bool showCaret = true;
    private float caretTimer = CaretBlinkInterval;

    private static readonly VirtualButton leftButton;
    private static readonly VirtualButton rightButton;
    private static readonly VirtualButton upButton;
    private static readonly VirtualButton downButton;

    private string? imeEditingText = null;
    private int imeEditingStart = 0;
    private int imeEditingLength = 0;

    private int selectedCompletionIndex = -1;

    private bool suppressCompletions;

    public string Text => buffer.Text;

    [MemberNotNullWhen(true, nameof(completions))]
    public bool HasCompletions => completions is { Count: > 0 };

    public int MaxTextLength { get; set; } = 64;

    static InputBox()
    {
        leftButton = new(new Binding() { Keyboard = [Keys.Left] }, Input.Gamepad, 0f, 0.4f);
        leftButton.SetRepeat(0.4f, 0.05f);

        rightButton = new(new Binding() { Keyboard = [Keys.Right] }, Input.Gamepad, 0f, 0.4f);
        rightButton.SetRepeat(0.4f, 0.05f);

        upButton = new(new Binding() { Keyboard = [Keys.Up] }, Input.Gamepad, 0f, 0.4f);
        upButton.SetRepeat(0.4f, 0.05f);

        downButton = new(new Binding() { Keyboard = [Keys.Down] }, Input.Gamepad, 0f, 0.4f);
        downButton.SetRepeat(0.4f, 0.05f);
    }

    public InputBox(ILiteralTextRenderer textRenderer, ICompletionProvider completionProvider, IChatInputHighlighter? highlighter = null)
    {
        this.textRenderer = textRenderer;
        this.completionProvider = completionProvider;
        this.highlighter = highlighter;

        buffer = new();
        buffer.TextOrCaretChanged += OnTextOrCaretChanged;
    }

    private void OnTextOrCaretChanged()
    {
        if (suppressCompletions)
        {
            suppressCompletions = false;
            completions = null;
            selectedCompletionIndex = -1;
            return;
        }
        completions = completionProvider.GetCompletions(buffer.TextBeforeCaret)?.ToList();
        selectedCompletionIndex = -1;
    }

    public void Activate()
    {
        TextInput.OnInput += OnCharInput;
        TextInputEXT.TextEditing += TextInputEXT_TextEditing;
    }

    public void Deactivate()
    {
        TextInput.OnInput -= OnCharInput;
        TextInputEXT.TextEditing -= TextInputEXT_TextEditing;
        buffer.Clear();
        selectedCompletionIndex = -1;
    }

    public void SetText(string text)
    {
        buffer.SetText(text);
        SetAlwaysShowCaretTimer();
    }

    public void Update()
    {
        if (!MInput.Keyboard.CurrentState.IsKeyDown(Keys.LeftShift) &&
            !MInput.Keyboard.CurrentState.IsKeyDown(Keys.RightShift) &&
            rightButton.Pressed)
        {
            rightButton.ConsumePress();
            if (buffer.MoveCaretForward())
                SetAlwaysShowCaretTimer();
        }
        else if (!MInput.Keyboard.CurrentState.IsKeyDown(Keys.LeftShift) &&
                 !MInput.Keyboard.CurrentState.IsKeyDown(Keys.RightShift) &&
                 leftButton.Pressed)
        {
            leftButton.ConsumePress();
            if (buffer.MoveCaretBackward())
                SetAlwaysShowCaretTimer();
        }
        else if (upButton.Pressed)
        {
            if (HasCompletions)
            {
                upButton.ConsumePress();
                if (selectedCompletionIndex == -1)
                {
                    selectedCompletionIndex = completions.Count - 1;
                }
                else
                {
                    selectedCompletionIndex--;
                    if (selectedCompletionIndex == -1)
                        selectedCompletionIndex = completions.Count - 1;
                }
            }
        }
        else if (downButton.Pressed)
        {
            if (HasCompletions)
            {
                downButton.ConsumePress();
                if (selectedCompletionIndex == -1)
                {
                    selectedCompletionIndex = 0;
                }
                else
                {
                    selectedCompletionIndex++;
                    selectedCompletionIndex %= completions.Count;
                }
            }
        }
        else if (MInput.Keyboard.Pressed(Keys.Tab))
        {
            if (completions is not null && (completions.Count == 1 || selectedCompletionIndex != -1))
            {
                Completion selected = completions[selectedCompletionIndex == -1 ? 0 : selectedCompletionIndex];
                string content = selected.Content;
                if (Text.Length - selected.Remove + content.Length > MaxTextLength)
                    content = content[..(MaxTextLength - Text.Length + selected.Remove)];
                SetSuppressCompletions();
                buffer.DoCompletion(selected.Remove, content);
            }
        }

        bool ctrlPressing = MInput.Keyboard.Check(Keys.LeftControl) ||
            MInput.Keyboard.Check(Keys.RightControl);

        if (MInput.Keyboard.Pressed(Keys.V) && ctrlPressing)
        {
            string text = TextInput.GetClipboardText();
            string textNoControl = new string(text.Where(c => !char.IsControl(c)).ToArray());

            if (Text.Length + textNoControl.Length > MaxTextLength)
                textNoControl = textNoControl[..(MaxTextLength - Text.Length)];

            if (!string.IsNullOrEmpty(textNoControl))
                buffer.InputString(textNoControl);
        }

        if (caretTimer > 0f)
        {
            caretTimer -= Engine.RawDeltaTime;
        }
        else
        {
            caretTimer = CaretBlinkInterval;
            showCaret = !showCaret;
        }
    }

    private void OnCharInput(char chr)
    {
        bool operated = false;
        if (char.IsControl(chr))
        {
            switch (chr)
            {
            case (char)8: operated = buffer.Backspace(); break; // backspace
            case (char)2: operated = buffer.MoveCaretToHome(); break; // home
            case (char)3: operated = buffer.MoveCaretToEnd(); break; // end
            case (char)127: operated = buffer.Delete(); break; // delete
            }

        }
        else
        {
            // TODO need we support surrogate pair?

            if (textRenderer.CanRender(chr) && Text.Length < MaxTextLength)
            {
                buffer.InputChar(chr);
                operated = true;
            }
        }
        if (operated)
            SetAlwaysShowCaretTimer();
    }

    private void TextInputEXT_TextEditing(string? text, int start, int length)
    {
        imeEditingText = text;
        imeEditingStart = start;
        imeEditingLength = length;
    }

    private void SetAlwaysShowCaretTimer()
    {
        showCaret = true;
        caretTimer = CaretBlinkInterval;
    }

    // any better ways?
    public void SetSuppressCompletions()
    {
        suppressCompletions = true;
    }

    private static int CountLines(string text)
    {
        int lines = 1;
        foreach (char c in text)
            if (c == '\n')
                lines++;
        return lines;
    }

    public void Render()
    {
        const float Margin = 16f;
        const float Padding = 8f;

        Vector2 baseLoc = new Vector2(Margin, Engine.Height - Margin);
        Vector2 textBaseLoc = baseLoc + new Vector2(Padding, -Padding);

        int lineCount = CountLines(buffer.Text);
        float lineHeight = textRenderer.LineHeight;
        float height = lineCount * lineHeight + 2 * Padding;
        Draw.Rect(
            position: baseLoc - Vector2.UnitY * height,
            width: Engine.Width - 2 * Margin,
            height: height,
            color: Color.Black * (0x7f / 255f)
        );

        IReadOnlyList<ChatInputHighlightSpan> spans = highlighter?.Highlight(buffer.Text, buffer.CaretPosition)
            ?? [new ChatInputHighlightSpan(0, buffer.Text.Length, Color.White)];

        // Anchor the last line to the bottom of the box, so extra lines grow upward.
        float literalY = textBaseLoc.Y - lineCount * lineHeight;

        int caretLine = 0;
        int caretLineStart = 0;
        for (int i = 0; i < buffer.CaretPosition; i++)
            if (buffer.Text[i] == '\n')
            {
                caretLine++;
                caretLineStart = i + 1;
            }

        float caretInlineWidth = textRenderer.MeasureLiteral(buffer.Text[caretLineStart..buffer.CaretPosition]).X;
        float caretLineTop = literalY + caretLine * lineHeight;

        Vector2 sizeImeEditing = Vector2.Zero;
        if (imeEditingText is not null)
        {
            textRenderer.DrawLiteral(buffer.Text, 0, buffer.CaretPosition, spans, new Vector2(textBaseLoc.X, literalY));

            Vector2 imePos = new Vector2(textBaseLoc.X + caretInlineWidth, caretLineTop);
            sizeImeEditing = textRenderer.MeasureLiteral(imeEditingText);
            textRenderer.DrawLiteral(imeEditingText, imePos, justify: Vector2.Zero, color: Color.Gray);

            int nextBreak = buffer.Text.IndexOf('\n', buffer.CaretPosition);
            int lineEnd = nextBreak < 0 ? buffer.Text.Length : nextBreak;
            textRenderer.DrawLiteral(
                buffer.Text, buffer.CaretPosition, lineEnd, spans,
                new Vector2(imePos.X + sizeImeEditing.X, caretLineTop)
            );
            if (nextBreak >= 0)
                textRenderer.DrawLiteral(
                    buffer.Text, nextBreak + 1, buffer.Text.Length, spans,
                    new Vector2(textBaseLoc.X, literalY + (caretLine + 1) * lineHeight)
                );
        }
        else
        {
            textRenderer.DrawLiteral(buffer.Text, 0, buffer.Text.Length, spans, new Vector2(textBaseLoc.X, literalY));
        }

        float caretWidth = caretInlineWidth;
        if (imeEditingText is not null)
        {
            Vector2 sizeBeforeImeStart = textRenderer.MeasureLiteral(
                imeEditingText.Substring(0, Math.Min(imeEditingStart, imeEditingText.Length))
            );
            caretWidth += sizeBeforeImeStart.X;
        }

        if (showCaret)
        {
            Vector2 fromLoc = new Vector2(textBaseLoc.X + caretWidth, caretLineTop + lineHeight);
            Vector2 toLoc = fromLoc - new Vector2(0f, lineHeight);

            Draw.Line(fromLoc, toLoc, Color.White, 2f);
        }

        {
            Vector2 view = new(Engine.ViewWidth, Engine.ViewHeight);
            float xScale = view.X / Engine.Width;
            float yScale = view.Y / Engine.Height;
            Vector2 viewPos = new Vector2(Engine.Viewport.X, Engine.Viewport.Y) +
                new Vector2((textBaseLoc.X + caretInlineWidth) * xScale, caretLineTop * yScale);
            Rectangle finalRect = new Rectangle(
                (int)viewPos.X,
                (int)viewPos.Y,
                Math.Max(1, (int)(sizeImeEditing.X * xScale)),
                (int)(lineHeight * yScale)
            );
            // Known issue: with only a few pinyin letters typed, the candidate window covers
            // the input row, and it only avoids it once more input arrives. A plain SDL2-only
            // project shows the same thing, so this is probably an SDL2-side issue.
            TextInputEXT.SetInputRectangle(finalRect);
        }

        if (HasCompletions)
        {
            const float CompletionsPadding = 4f;
            Vector2 cBaseLoc = new Vector2(textBaseLoc.X + caretWidth, caretLineTop - Padding);
            Vector2 cTextBaseLoc = cBaseLoc + new Vector2(CompletionsPadding, -CompletionsPadding);
            float width = 0f;
            float totalHeight = textRenderer.LineHeight * completions.Count;
            foreach (var item in completions)
            {
                Vector2 size = textRenderer.Measure(item.Display);
                width = Math.Max(width, size.X);
            }
            float cX = cBaseLoc.X;
            float cY = cBaseLoc.Y - totalHeight - CompletionsPadding * 2f;
            float cW = width + CompletionsPadding * 2f;
            float cH = totalHeight + CompletionsPadding * 2f;
            Draw.Rect(cX, cY, cW, cH, Color.Black * (0xaa / 255f));
            Draw.Rect(cX, cY, cW, 1f, Color.Cyan);
            Draw.Rect(cX - 3f, cY, 3f, cH, Color.CornflowerBlue);
            float curY = cTextBaseLoc.Y;
            for (int i = completions.Count - 1; i >= 0; i--)
            {
                bool selected = i == selectedCompletionIndex;
                Color c = selected ? Color.White : Color.LightGray;
                if (selected)
                {
                    float sX = cBaseLoc.X;
                    float sY = curY - textRenderer.LineHeight;
                    float sW = cW;
                    float sH = textRenderer.LineHeight;
                    Draw.Rect(sX, sY, sW, sH, Color.Wheat * (0x22 / 255f));
                    Draw.Rect(sX - 3f, sY, 3f, sH, Color.Wheat);
                }
                textRenderer.Draw(completions[i].Display, new Vector2(cTextBaseLoc.X, curY), Vector2.UnitY, c);
                curY -= textRenderer.LineHeight;
            }
        }
    }
}
