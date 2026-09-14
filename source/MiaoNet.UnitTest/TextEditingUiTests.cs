using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Chat;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace MiaoNet.UnitTest;

// the text editing kernel, plus the input box and completion popup geometry from the
// parameter spec
[TestClass]
public sealed class TextEditingUiTests
{
    private const float LineHeight = 24f;
    private const float CharWidth = 10f;

    private static void AssertClose(float expected, float actual, string message)
        => Assert.IsLessThan(1e-4f, MathF.Abs(expected - actual), $"{message}: expected {expected}, got {actual}");

    private sealed class FakeTextRenderer : ITextRenderer
    {
        public UiSize Measure(string text, TextStyle style)
            => new(text.Length * CharWidth * style.Scale, 12f * style.Scale);

        public void Draw(IUiCanvas canvas, string text, UiOffset position, TextStyle style)
        {
        }

        public bool CanRender(int character, TextStyle style) => true;
    }

    private sealed class FakeCompletionProvider : ICompletionProvider
    {
        public List<Completion> Candidates { get; } = [];

        public string? LastQuery { get; private set; }

        public int QueryCount { get; private set; }

        public IEnumerable<Completion>? GetCompletions(string input)
        {
            LastQuery = input;
            QueryCount++;
            return Candidates.Count == 0 ? null : Candidates;
        }
    }

    // ---------------------------------------------------------------- editing kernel

    [TestMethod]
    public void InputChar_AppendsAndAdvancesTheCaret()
    {
        var controller = new TextEditingController(new FakeCompletionProvider());

        controller.InputChar('a');
        controller.InputChar('b');

        Assert.AreEqual("ab", controller.Text);
        Assert.AreEqual(2, controller.CaretPosition);
    }

    [TestMethod]
    public void InputChar_RespectsMaxTextLength()
    {
        var controller = new TextEditingController(new FakeCompletionProvider()) { MaxTextLength = 3 };

        foreach (char c in "abcde")
        {
            controller.InputChar(c);
        }

        Assert.AreEqual("abc", controller.Text);
    }

    [TestMethod]
    public void ControlChars_MapToTheOriginalEditingCommands()
    {
        var controller = new TextEditingController(new FakeCompletionProvider());
        controller.SetText("abc");

        Assert.IsTrue(controller.InputChar((char)8), "backspace");
        Assert.AreEqual("ab", controller.Text);

        Assert.IsTrue(controller.InputChar((char)2), "home");
        Assert.AreEqual(0, controller.CaretPosition);

        Assert.IsTrue(controller.InputChar((char)127), "delete");
        Assert.AreEqual("b", controller.Text);

        Assert.IsTrue(controller.InputChar((char)3), "end");
        Assert.AreEqual(1, controller.CaretPosition);
    }

    [TestMethod]
    public void Paste_FiltersControlCharactersAndClampsToMaxLength()
    {
        var controller = new TextEditingController(new FakeCompletionProvider()) { MaxTextLength = 5 };

        controller.Paste("a\nb\tc");
        Assert.AreEqual("abc", controller.Text);

        controller.Paste("xxxxxxxx");
        Assert.AreEqual("abcxx", controller.Text);
    }

    [TestMethod]
    public void CaretBlink_TogglesAfterTheInterval()
    {
        var controller = new TextEditingController(new FakeCompletionProvider());
        controller.Activate();

        Assert.IsTrue(controller.ShowCaret, "caret visible while typing");

        // The timer is consumed first; the blink flips on the following frame.
        controller.Update(TextEditingController.CaretBlinkInterval);
        Assert.IsTrue(controller.ShowCaret, "still visible on the frame the timer expires");

        controller.Update(0.001f);
        Assert.IsFalse(controller.ShowCaret, "caret hidden");

        controller.Update(TextEditingController.CaretBlinkInterval);
        controller.Update(0.001f);
        Assert.IsTrue(controller.ShowCaret, "caret visible again");
    }

    [TestMethod]
    public void Caret_IsHiddenWhenNotFocused()
    {
        var controller = new TextEditingController(new FakeCompletionProvider());

        Assert.IsFalse(controller.ShowCaret, "unfocused");
        controller.Activate();
        Assert.IsTrue(controller.ShowCaret, "focused");
    }

    [TestMethod]
    public void Ime_CompositionIsTracked()
    {
        var controller = new TextEditingController(new FakeCompletionProvider());
        controller.SetText("ab");

        controller.SetImeComposition("にほ", 0, 2);

        Assert.AreEqual("にほ", controller.ImeText);
        Assert.AreEqual(0, controller.ImeStart);
        Assert.AreEqual(2, controller.ImeLength);
        Assert.AreEqual("ab", controller.Text, "composition is not committed into the buffer");

        controller.SetImeComposition(null, 0, 0);
        Assert.IsNull(controller.ImeText, "composition cleared");
    }

    // ---------------------------------------------------------------- completion

    [TestMethod]
    public void Completion_IsQueriedWithTheTextBeforeTheCaret()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("hello", "hello", 0));
        var controller = new TextEditingController(provider);

        controller.InputChar('h');
        controller.InputChar('e');

        Assert.IsTrue(controller.HasCompletions);
        Assert.AreEqual("he", provider.LastQuery);
    }

    [TestMethod]
    public void Completion_NavigationWrapsInBothDirections()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("a", "a", 0));
        provider.Candidates.Add(new Completion("b", "b", 0));
        provider.Candidates.Add(new Completion("c", "c", 0));
        var controller = new TextEditingController(provider);
        controller.InputChar('x');

        controller.SelectNextCompletion();
        Assert.AreEqual(0, controller.SelectedCompletionIndex, "first Down selects the first item");
        controller.SelectNextCompletion();
        Assert.AreEqual(1, controller.SelectedCompletionIndex);
        controller.SelectNextCompletion();
        Assert.AreEqual(2, controller.SelectedCompletionIndex);
        controller.SelectNextCompletion();
        Assert.AreEqual(0, controller.SelectedCompletionIndex, "wraps to the start");

        controller.SelectPreviousCompletion();
        Assert.AreEqual(2, controller.SelectedCompletionIndex, "wraps to the end");
    }

    [TestMethod]
    public void Completion_AcceptRequiresASingleCandidateOrAnExplicitSelection()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("hello", "hello", 2));
        provider.Candidates.Add(new Completion("help", "help", 2));
        var controller = new TextEditingController(provider);
        controller.SetText("he");

        Assert.IsFalse(controller.AcceptCompletion(), "two candidates and nothing selected");

        controller.SelectNextCompletion();
        Assert.IsTrue(controller.AcceptCompletion(), "explicit selection");
        Assert.AreEqual("hello", controller.Text);
    }

    [TestMethod]
    public void Completion_SingleCandidateIsAcceptedWithoutSelection()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("/help", "/help", 1));
        var controller = new TextEditingController(provider);
        controller.InputChar('/');

        Assert.IsTrue(controller.AcceptCompletion());
        Assert.AreEqual("/help", controller.Text);
    }

    [TestMethod]
    public void Completion_AcceptClampsToMaxTextLength()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("/verylongcommand", "/verylongcommand", 0));
        var controller = new TextEditingController(provider) { MaxTextLength = 5 };
        controller.InputChar('/');

        controller.AcceptCompletion();

        Assert.AreEqual(5, controller.Text.Length);
    }

    [TestMethod]
    public void Completion_SuppressPreventsTheNextQuery()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("x", "x", 0));
        var controller = new TextEditingController(provider);

        controller.SetSuppressCompletions();
        controller.SetText("hello");

        Assert.IsFalse(controller.HasCompletions, "suppressed query produced no candidates");
        Assert.AreEqual(0, provider.QueryCount, "provider was not queried at all");
    }

    // ---------------------------------------------------------------- input box geometry

    private static (UiRoot Ui, ChatInputNode Input, TextFieldNode Field, CompletionPopupNode Popup) BuildInputBox(
        TextEditingController controller,
        bool focused,
        float viewportWidth = 400f,
        float viewportHeight = 300f)
    {
        var renderer = new FakeTextRenderer();
        var field = new TextFieldNode(renderer, controller) { LineHeight = LineHeight, Scale = 1f };
        var popup = new CompletionPopupNode(renderer) { LineHeight = LineHeight, Scale = 1f };

        // UIComponent binds the candidate list each frame; mirror that here.
        popup.Items = (IReadOnlyList<Completion>?)controller.Completions ?? [];
        popup.SelectedIndex = controller.SelectedCompletionIndex;

        var input = new ChatInputNode(field, popup) { LineHeight = LineHeight, Scale = 1f };

        var host = new AlignNode
        {
            Alignment = UiAlignment.TopLeft,
            Child = input,
            Style = new UiStyle { Width = viewportWidth, Height = viewportHeight },
        };

        var ui = new UiRoot();
        ui.SetRoot(host);
        if (focused)
        {
            controller.Activate();
        }
        ui.Layout(viewportWidth, viewportHeight);
        return (ui, input, field, popup);
    }

    [TestMethod]
    public void InputBox_UsesLineHeightPlusPaddingAsItsHeight()
    {
        var controller = new TextEditingController(new FakeCompletionProvider());
        (_, ChatInputNode input, TextFieldNode field, _) = BuildInputBox(controller, focused: true);

        // height = lh + 2 * Padding = 24 + 16 = 40
        AssertClose(40f, input.Bounds.Height, "input height");
        // text origin is Padding inside the box
        AssertClose(8f, field.Bounds.X, "text origin X");
        AssertClose(8f, field.Bounds.Y, "text origin Y");
        AssertClose(LineHeight, field.Bounds.Height, "text area height");
    }

    [TestMethod]
    public void CompletionPopup_SitsOnTheBoxTopEdgeAtTheCaret()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("aaa", "aaa", 0));
        provider.Candidates.Add(new Completion("bb", "bb", 0));
        var controller = new TextEditingController(provider);
        controller.SetText("abc");

        (_, ChatInputNode input, TextFieldNode field, CompletionPopupNode popup) =
            BuildInputBox(controller, focused: true);

        // "abc" is 30 wide with the deterministic renderer; the popup starts Padding past the box.
        AssertClose(30f, field.TextBeforeCaretWidth, "text before caret width");

        Assert.IsTrue(popup.IsVisible, "popup visible while focused with candidates");
        // popup bottom sits on the box top edge, left at box + Padding + caret offset
        AssertClose(0f, popup.Bounds.Bottom, "popup bottom on the box top edge");
        AssertClose(8f + 30f, popup.Bounds.X, "popup X at the caret");
        AssertClose(38f, popup.Bounds.Width, "popup width = widest item + 2 * Padding");
        AssertClose((LineHeight * 2f) + 8f, popup.Bounds.Height, "popup height = rows + 2 * Padding");
    }

    [TestMethod]
    public void CompletionPopup_IsHiddenWithoutFocus()
    {
        var provider = new FakeCompletionProvider();
        provider.Candidates.Add(new Completion("aaa", "aaa", 0));
        var controller = new TextEditingController(provider);
        controller.InputChar('a');

        (_, _, _, CompletionPopupNode popup) = BuildInputBox(controller, focused: false);

        Assert.IsFalse(popup.IsVisible, "unfocused field shows no candidates");
    }
}
