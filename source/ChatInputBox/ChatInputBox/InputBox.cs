using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class InputBox
{
    public const float CaretBlinkInterval = 0.5f;

    private readonly ITextRenderer textRenderer;
    private readonly ICompletionProvider completionProvider;

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

    public InputBox(ITextRenderer textRenderer, ICompletionProvider completionProvider)
    {
        this.textRenderer = textRenderer;
        this.completionProvider = completionProvider;

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
        if (rightButton.Pressed)
        {
            rightButton.ConsumePress();
            if (buffer.MoveCaretForward())
                SetAlwaysShowCaretTimer();
        }
        else if (leftButton.Pressed)
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

    public void Render()
    {
        const float Margin = 16f;
        const float Padding = 8f;

        Vector2 baseLoc = new Vector2(Margin, Engine.Height - Margin);
        Vector2 textBaseLoc = baseLoc + new Vector2(Padding, -Padding);

        float height = textRenderer.LineHeight + 2 * Padding;
        Draw.Rect(
            position: baseLoc - Vector2.UnitY * height,
            width: Engine.Width - 2 * Margin,
            height: height,
            color: Color.Black * (0x7f / 255f)
        );

        Vector2 pos = textBaseLoc;
        Vector2 sizeBeforeCaret = textRenderer.Measure(buffer.TextBeforeCaret);
        Vector2 sizeAfterCaret = textRenderer.Measure(buffer.TextAfterCaret);
        textRenderer.Draw(buffer.TextBeforeCaret, pos, justify: new Vector2(0f, 1f), color: Color.White);
        pos.X += sizeBeforeCaret.X;

        Vector2 sizeImeEditing = Vector2.Zero;
        if (imeEditingText is not null)
        {
            sizeImeEditing = textRenderer.Measure(imeEditingText);
            textRenderer.Draw(imeEditingText, pos, justify: new Vector2(0f, 1f), color: Color.Gray);
            pos.X += sizeImeEditing.X;
        }
        textRenderer.Draw(buffer.TextAfterCaret, pos, justify: new Vector2(0f, 1f), color: Color.White);
        pos.X += sizeAfterCaret.X;

        if (showCaret)
        {
            float width = sizeBeforeCaret.X;
            if (imeEditingText is not null)
            {
                Vector2 sizeBeforeImeStart = textRenderer.Measure(imeEditingText.Substring(0, Math.Min(imeEditingStart, imeEditingText.Length)));
                width += sizeBeforeImeStart.X;
            }

            Vector2 fromLoc = textBaseLoc + new Vector2(width, 0f);
            Vector2 toLoc = fromLoc - new Vector2(0f, textRenderer.LineHeight);

            Draw.Line(fromLoc, toLoc, Color.White, 2f);
        }

        {
            Vector2 view = new(Engine.ViewWidth, Engine.ViewHeight);
            float xScale = view.X / Engine.Width;
            float yScale = view.Y / Engine.Height;
            Vector2 viewPos = new((textBaseLoc.X + sizeBeforeCaret.X) * xScale, (baseLoc.Y - height) * yScale);
            Rectangle finalRect = new Rectangle(
                (int)viewPos.X,
                (int)viewPos.Y,
                Math.Max(1, (int)(sizeImeEditing.X * xScale)),
                (int)(height * yScale)
            );
            // TODO the calculated result is almost correct but
            // IME is still being placed in somewhere incorrect
            TextInputEXT.SetInputRectangle(finalRect);
        }

        if (HasCompletions)
        {
            const float CompletionsPadding = 4f;
            Vector2 cBaseLoc = textBaseLoc + new Vector2(sizeBeforeCaret.X, -textRenderer.LineHeight - Padding);
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