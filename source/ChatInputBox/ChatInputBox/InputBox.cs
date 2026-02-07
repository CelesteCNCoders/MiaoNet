#nullable enable

using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ChatInputBox;

public sealed class InputBox
{
    public const float CaretBlinkInterval = 0.5f;

    private readonly ITextRenderer textRenderer;
    private readonly TextBuffer buffer;
    private bool showCaret = true;
    private float caretTimer = CaretBlinkInterval;

    private static readonly VirtualButton leftButton;
    private static readonly VirtualButton rightButton;

    private string? imeEditingText = null;
    private int imeEditingStart = 0;
    private int imeEditingLength = 0;

    public string Text => buffer.Text;

    static InputBox()
    {
        leftButton = new(new Binding() { Keyboard = [Keys.Left] }, Input.Gamepad, 0f, 0.4f);
        leftButton.SetRepeat(0.4f, 0.05f);
        rightButton = new(new Binding() { Keyboard = [Keys.Right] }, Input.Gamepad, 0f, 0.4f);
        rightButton.SetRepeat(0.4f, 0.05f);
    }

    public InputBox(ITextRenderer textRenderer)
    {
        buffer = new();
        this.textRenderer = textRenderer;
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
            if (buffer.ForwardCaret())
                SetAlwaysShowCaretTimer();
        }
        else if (leftButton.Pressed)
        {
            leftButton.ConsumePress();
            if (buffer.BackwardCaret())
                SetAlwaysShowCaretTimer();
        }

        bool ctrlPressing = MInput.Keyboard.Check(Keys.LeftControl) ||
            MInput.Keyboard.Check(Keys.RightControl);

        if (MInput.Keyboard.Pressed(Keys.V) && ctrlPressing)
        {
            string text = TextInput.GetClipboardText();
            string textNoControl = new string(text.Where(c => !char.IsControl(c)).ToArray());
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
            case (char)2: operated = buffer.BackwardToHomeCaret(); break; // home
            case (char)3: operated = buffer.ForwardToEndCaret(); break; // end
            case (char)127: operated = buffer.Delete(); break; // delete
            }

        }
        else
        {
            // TODO need we support surrogate pair?

            if (textRenderer.CanRender(chr))
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
            color: Color.Black with { A = 100 }
        );

        // unluckly we need to substring here since there's no ReadOnlySpan<char> overload...
        // should we cache the sliced string?
        string strBeforeCaret = buffer.Text.Substring(0, buffer.CaretPosition);
        string strAfterCaret = buffer.Text.Substring(buffer.CaretPosition);

        Vector2 pos = textBaseLoc;
        Vector2 sizeBeforeCaret = textRenderer.Measure(strBeforeCaret);
        Vector2 sizeAfterCaret = textRenderer.Measure(strAfterCaret);
        textRenderer.Draw(strBeforeCaret, pos, justify: new Vector2(0f, 1f), color: Color.White);
        pos.X += sizeBeforeCaret.X;
        if (imeEditingText is not null)
        {
            Vector2 sizeImeEditing = textRenderer.Measure(imeEditingText);
            textRenderer.Draw(imeEditingText, pos, justify: new Vector2(0f, 1f), color: Color.Gray);
            pos.X += sizeImeEditing.X;
        }
        textRenderer.Draw(strAfterCaret, pos, justify: new Vector2(0f, 1f), color: Color.White);
        pos.X += sizeAfterCaret.X;

        if (showCaret)
        {
            float width = sizeBeforeCaret.X;
            if (imeEditingText is not null)
            {
                Vector2 sizeBeforeImeStart = textRenderer.Measure(imeEditingText.Substring(0, imeEditingStart));
                width += sizeBeforeImeStart.X;
            }

            Vector2 fromLoc = textBaseLoc + new Vector2(width, 0);
            Vector2 toLoc = fromLoc - new Vector2(0f, textRenderer.LineHeight);

            Draw.Line(fromLoc, toLoc, Color.White, 2f);
        }
        Vector2 view = new(Engine.ViewWidth, Engine.ViewHeight);
        Vector2 viewPos = new(pos.X / Engine.Width * view.X, pos.Y / Engine.Height * view.Y);
        // TODO set the value correctly
        TextInputEXT.SetInputRectangle(new Rectangle((int)viewPos.X + Engine.ViewPadding + 72, (int)viewPos.Y + Engine.ViewPadding, 1, 0));
    }
}