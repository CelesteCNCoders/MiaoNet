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
    }

    public void Deactivate()
    {
        TextInput.OnInput -= OnCharInput;
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

        textRenderer.Draw(
            buffer.Text,
            textBaseLoc,
            justify: new Vector2(0f, 1f),
            color: Color.White
        );

        if (showCaret)
        {
            Vector2 beforeCaret = textRenderer.Measure(buffer.Text.Substring(0, buffer.CaretPosition));
            float width = beforeCaret.X;

            Vector2 fromLoc = textBaseLoc + new Vector2(width, 0);
            Vector2 toLoc = fromLoc - new Vector2(0f, textRenderer.LineHeight);

            Draw.Line(fromLoc, toLoc, Color.White, 2f);
        }
    }
}