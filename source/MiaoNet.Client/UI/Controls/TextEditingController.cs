using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.MiaoNet.Chat;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// the text editing kernel for the chat input: text, caret, ime composition, completion
// selection and caret blinking.
//
// callers push input in (chars, paste, caret moves, ime updates) instead of us polling MInput,
// so it stays free of game types and testable. the text model is TextBuffer.
public sealed class TextEditingController
{
    // CHAT.INPUT.CARET_BLINK, in seconds
    public const float CaretBlinkInterval = 0.5f;

    private readonly TextBuffer buffer = new();
    private readonly ICompletionProvider completionProvider;
    private readonly Func<int, bool>? canRender;

    private List<Completion>? completions;
    private int selectedCompletionIndex = -1;
    private bool suppressCompletions;
    private bool showCaret = true;
    private float caretTimer = CaretBlinkInterval;

    private string? imeText;
    private int imeStart;
    private int imeLength;

    public TextEditingController(ICompletionProvider completionProvider, Func<int, bool>? canRender = null)
    {
        ArgumentNullException.ThrowIfNull(completionProvider);
        this.completionProvider = completionProvider;
        this.canRender = canRender;
        buffer.TextOrCaretChanged += OnTextOrCaretChanged;
    }

    // raised when the text, caret, completion list or caret visibility changed
    public event Action? StateChanged;

    // CHAT.INPUT.MAX_LEN
    public int MaxTextLength { get; set; } = 64;

    // whether the field owns keyboard focus; the caret only blinks while focused
    public bool Focused { get; private set; }

    public string Text => buffer.Text;

    public string TextBeforeCaret => buffer.TextBeforeCaret;

    public string TextAfterCaret => buffer.TextAfterCaret;

    public int CaretPosition => buffer.CaretPosition;

    public bool ShowCaret => showCaret && Focused;

    // ime composition string, or null when no composition is in progress
    public string? ImeText => imeText;

    public int ImeStart => imeStart;

    public int ImeLength => imeLength;

    public IReadOnlyList<Completion>? Completions => completions;

    public int SelectedCompletionIndex => selectedCompletionIndex;

    public bool HasCompletions => completions is { Count: > 0 };

    public void Activate()
    {
        if (Focused)
        {
            return;
        }

        Focused = true;
        SetAlwaysShowCaretTimer();
        RaiseStateChanged();
    }

    public void Deactivate()
    {
        if (!Focused)
        {
            return;
        }

        Focused = false;
        Clear();
        RaiseStateChanged();
    }

    // advances the caret blink; only the caret visibility depends on this
    public void Update(float deltaTime)
    {
        if (caretTimer > 0f)
        {
            caretTimer -= deltaTime;
            return;
        }

        caretTimer = CaretBlinkInterval;
        showCaret = !showCaret;
        RaiseStateChanged();
    }

    public void Clear()
    {
        suppressCompletions = false;
        completions = null;
        selectedCompletionIndex = -1;
        imeText = null;
        imeStart = 0;
        imeLength = 0;
        buffer.Clear();
    }

    public void SetText(string text)
    {
        buffer.SetText(text);
        SetAlwaysShowCaretTimer();
    }

    // handles one char from the text input event, including its control-char bindings
    public bool InputChar(char chr)
    {
        bool operated = false;

        if (char.IsControl(chr))
        {
            switch (chr)
            {
                case (char)8: // backspace
                    operated = buffer.Backspace();
                    break;
                case (char)2: // home
                    operated = buffer.MoveCaretToHome();
                    break;
                case (char)3: // end
                    operated = buffer.MoveCaretToEnd();
                    break;
                case (char)127: // delete
                    operated = buffer.Delete();
                    break;
            }
        }
        else if ((canRender?.Invoke(chr) ?? true) && Text.Length < MaxTextLength)
        {
            buffer.InputChar(chr);
            operated = true;
        }

        if (operated)
        {
            SetAlwaysShowCaretTimer();
        }

        return operated;
    }

    // handles a paste, dropping control chars and clamping to MaxTextLength
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string filtered = new(text.Where(c => !char.IsControl(c)).ToArray());
        if (Text.Length + filtered.Length > MaxTextLength)
        {
            filtered = filtered[..Math.Max(0, MaxTextLength - Text.Length)];
        }

        if (string.IsNullOrEmpty(filtered))
        {
            return;
        }

        buffer.InputString(filtered);
        SetAlwaysShowCaretTimer();
    }

    public bool MoveCaretBackward()
    {
        bool moved = buffer.MoveCaretBackward();
        if (moved)
        {
            SetAlwaysShowCaretTimer();
        }
        return moved;
    }

    public bool MoveCaretForward()
    {
        bool moved = buffer.MoveCaretForward();
        if (moved)
        {
            SetAlwaysShowCaretTimer();
        }
        return moved;
    }

    public void MoveCaretToHome()
    {
        buffer.MoveCaretToHome();
        SetAlwaysShowCaretTimer();
    }

    public void MoveCaretToEnd()
    {
        buffer.MoveCaretToEnd();
        SetAlwaysShowCaretTimer();
    }

    // updates the ime composition; null clears it
    public void SetImeComposition(string? text, int start, int length)
    {
        imeText = text;
        imeStart = start;
        imeLength = length;
        RaiseStateChanged();
    }

    // moves the completion selection down, wrapping at the end
    public void SelectNextCompletion()
    {
        if (completions is not { Count: > 0 } list)
        {
            return;
        }

        selectedCompletionIndex = selectedCompletionIndex == -1
            ? 0
            : (selectedCompletionIndex + 1) % list.Count;
        RaiseStateChanged();
    }

    // moves the completion selection up, wrapping at the start
    public void SelectPreviousCompletion()
    {
        if (completions is not { Count: > 0 } list)
        {
            return;
        }

        if (selectedCompletionIndex == -1)
        {
            selectedCompletionIndex = list.Count - 1;
        }
        else
        {
            selectedCompletionIndex--;
            if (selectedCompletionIndex == -1)
            {
                selectedCompletionIndex = list.Count - 1;
            }
        }

        RaiseStateChanged();
    }

    // applies the selected completion: only with a single candidate, or when the user already
    // picked one
    public bool AcceptCompletion()
    {
        if (completions is not { Count: > 0 } list)
        {
            return false;
        }

        if (list.Count != 1 && selectedCompletionIndex == -1)
        {
            return false;
        }

        Completion selected = list[selectedCompletionIndex == -1 ? 0 : selectedCompletionIndex];
        string content = selected.Content;

        int room = MaxTextLength - Text.Length + selected.Remove;
        if (Text.Length - selected.Remove + content.Length > MaxTextLength)
        {
            content = content[..Math.Max(0, room)];
        }

        SetSuppressCompletions();
        buffer.DoCompletion(selected.Remove, content);
        SetAlwaysShowCaretTimer();
        return true;
    }

    // suppresses the next completion query, used when text is set programmatically
    public void SetSuppressCompletions() => suppressCompletions = true;

    private void OnTextOrCaretChanged()
    {
        if (suppressCompletions)
        {
            suppressCompletions = false;
            completions = null;
            selectedCompletionIndex = -1;
            RaiseStateChanged();
            return;
        }

        completions = completionProvider.GetCompletions(buffer.TextBeforeCaret)?.ToList();
        selectedCompletionIndex = -1;
        RaiseStateChanged();
    }

    private void SetAlwaysShowCaretTimer()
    {
        showCaret = true;
        caretTimer = CaretBlinkInterval;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();
}
