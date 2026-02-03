using System.Collections.Immutable;
using Celeste.Mod.ChatInputBox;

namespace ChatInputBox.UnitTest;

// don't read these to study something, these were written by ai

[TestClass]
public sealed class ChatTextParsingTest
{
    private static readonly ImmutableArray<Color> TestCommonColors = ChatText.CommonColors;
    private readonly Color DefaultColor = TestCommonColors[7]; // White

    [TestMethod]
    public void Parse_EmptyInput_ReturnsEmptySegments()
    {
        var result = ChatText.Parse("", DefaultColor);
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void Parse_PlainText_NoEscape_ReturnsSingleSegment()
    {
        var result = ChatText.Parse("Hello world", DefaultColor);
        Assert.HasCount(1, result);
        var seg = result[0];
        Assert.AreEqual(ChatTextStyle.None, seg.Style);
        Assert.AreEqual(DefaultColor, seg.Color);
        Assert.AreEqual("Hello world", seg.Text);
    }

    [TestMethod]
    public void Parse_EscapeBackslash_BecomesSingleBackslash()
    {
        var result = ChatText.Parse(@"a\\b", DefaultColor);
        Assert.HasCount(1, result);
        Assert.AreEqual(@"a\b", result[0].Text);
        Assert.AreEqual(DefaultColor, result[0].Color);
        Assert.AreEqual(ChatTextStyle.None, result[0].Style);
    }

    [TestMethod]
    public void Parse_UnknownEscape_SequenceTreatedAsLiteral()
    {
        var result = ChatText.Parse(@"a\xb\yc", DefaultColor);
        Assert.HasCount(1, result);
        Assert.AreEqual(@"a\xb\yc", result[0].Text);
    }

    [TestMethod]
    public void Parse_ResetCommand_ResetsToDefaultColorAndStyle()
    {
        var input = @"\1Red\uUnderline\rBackToDefault";
        var result = ChatText.Parse(input, DefaultColor);

        Assert.HasCount(3, result);
        AssertSegment(result[0], ChatTextStyle.None, TestCommonColors[1], "Red");
        AssertSegment(result[1], ChatTextStyle.Underscore, TestCommonColors[1], "Underline");
        AssertSegment(result[2], ChatTextStyle.None, DefaultColor, "BackToDefault");
    }

    [TestMethod]
    public void Parse_DigitColorCodes_0To9()
    {
        for (int i = 0; i < 10; i++)
        {
            var input = $"\\{i}Text";
            var result = ChatText.Parse(input, DefaultColor);
            Assert.HasCount(1, result);
            Assert.AreEqual(TestCommonColors[i], result[0].Color);
            Assert.AreEqual("Text", result[0].Text);
        }
    }

    [TestMethod]
    public void Parse_HexColorCodes_aTo_f_ATo_F()
    {
        char[] hexChars = ['a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F'];
        for (int i = 0; i < hexChars.Length; i++)
        {
            char c = hexChars[i];
            int index = (c >= 'a') ? 10 + (c - 'a') : 10 + (c - 'A');
            var input = $"\\{c}Text";
            var result = ChatText.Parse(input, DefaultColor);
            Assert.HasCount(1, result);
            Assert.AreEqual(TestCommonColors[index], result[0].Color);
            Assert.AreEqual("Text", result[0].Text);
        }
    }

    [TestMethod]
    public void Parse_ValidHexColor_CustomColor()
    {
        var result = ChatText.Parse(@"\#FF00AAHello", DefaultColor);
        Assert.HasCount(1, result);
        var expectedColor = Color.FromArgb(0xFF, 0x00, 0xAA);
        Assert.AreEqual(expectedColor, result[0].Color);
        Assert.AreEqual("Hello", result[0].Text);
    }

    [TestMethod]
    public void Parse_InvalidHexColor_TreatedAsLiteral()
    {
        var cases = new[]
        {
            @"\#GGGGGGtext",
            @"\#12345text",   // too short
            @"\#12p4567text", // too long (but parser only reads 6, so this is valid! → actually valid)
            @"\#XYZ123text",
            @"\#text"         // not enough chars
        };

        foreach (var input in cases)
        {
            var result = ChatText.Parse(input, DefaultColor);
            Assert.HasCount(1, result);
            Assert.AreEqual(input, result[0].Text); // entire thing is literal
        }
    }

    [TestMethod]
    public void Parse_UnderlineAndStrikethrough_Combo()
    {
        var result = ChatText.Parse(@"Normal\uUnderline\sBoth", DefaultColor);
        Assert.HasCount(3, result);
        AssertSegment(result[0], ChatTextStyle.None, DefaultColor, "Normal");
        AssertSegment(result[1], ChatTextStyle.Underscore, DefaultColor, "Underline");
        AssertSegment(result[2],
            ChatTextStyle.Underscore | ChatTextStyle.Strikethrough,
            DefaultColor, "Both");
    }

    [TestMethod]
    public void Parse_ConsecutiveControlSequences_NoEmptySegments()
    {
        var result = ChatText.Parse(@"\1\u\sStart", DefaultColor);
        Assert.HasCount(1, result);
        AssertSegment(result[0],
            ChatTextStyle.Underscore | ChatTextStyle.Strikethrough,
            TestCommonColors[1],
            "Start");
    }

    [TestMethod]
    public void Parse_ControlSequenceAtEnd_NoTrailingEmptySegment()
    {
        var result = ChatText.Parse(@"Text\1", DefaultColor);
        Assert.HasCount(1, result);
        AssertSegment(result[0], ChatTextStyle.None, DefaultColor, "Text");
    }

    [TestMethod]
    public void Parse_MixedValidAndInvalidEscapes()
    {
        var result = ChatText.Parse(@"Good\1part\Xbad\#ZZZZZZend", DefaultColor);
        // \1 is valid → split
        // \X and \#ZZZZZZ are invalid → literal
        Assert.HasCount(2, result);
        AssertSegment(result[0], ChatTextStyle.None, DefaultColor, "Good");
        AssertSegment(result[1], ChatTextStyle.None, TestCommonColors[1], @"part\Xbad\#ZZZZZZend");
    }

    [TestMethod]
    public void Parse_SingleCharacterInput()
    {
        var result = ChatText.Parse("A", DefaultColor);
        Assert.HasCount(1, result);
        Assert.AreEqual("A", result[0].Text);
    }

    [TestMethod]
    public void Parse_SingleEscapeAtStart()
    {
        var result = ChatText.Parse(@"\1", DefaultColor);
        Assert.IsEmpty(result); // no text after control
    }

    [TestMethod]
    public void Parse_DefaultColorIsUsedWhenNoStyleSet()
    {
        var customDefault = Color.FromArgb(100, 100, 100);
        var result = ChatText.Parse("Hello", customDefault);
        Assert.AreEqual(customDefault, result[0].Color);
    }

    [TestMethod]
    public void Parse_LongTextWithMultipleStateChanges()
    {
        var input = @"Start\1Red\uUL\#00FF00Green\sStrike\rReset\2Final";
        var result = ChatText.Parse(input, DefaultColor);

        Assert.HasCount(7, result);
        AssertSegment(result[0], ChatTextStyle.None, DefaultColor, "Start");
        AssertSegment(result[1], ChatTextStyle.None, TestCommonColors[1], "Red");
        AssertSegment(result[2], ChatTextStyle.Underscore, TestCommonColors[1], "UL");
        AssertSegment(result[3], ChatTextStyle.Underscore, Color.FromArgb(0, 255, 0), "Green");
        AssertSegment(result[4],
            ChatTextStyle.Underscore | ChatTextStyle.Strikethrough,
            Color.FromArgb(0, 255, 0), "Strike");
        AssertSegment(result[5], ChatTextStyle.None, DefaultColor, "Reset");
        AssertSegment(result[6], ChatTextStyle.None, TestCommonColors[2], "Final");
    }

    [TestMethod]
    public void Parse_ComplexStateToggles_HandlesCorrectly()
    {
        var input = @"\u\u\s\#ffffff\sTarget";
        var result = ChatText.Parse(input, new(0, 0, 0, 0));

        Assert.HasCount(1, result);
        Assert.AreEqual("Target", result[0].Text);
        Assert.AreEqual(ChatTextStyle.None, result[0].Style);
        Assert.AreEqual(new Color(255, 255, 255, 255), result[0].Color);
    }

    [TestMethod]
    public void Parse_PseudoEscapeAndTrailingSlash_DoesNotCrash()
    {
        var input = @"\#abcNormal\\End\";
        var result = ChatText.Parse(input, new(0, 0, 0, 0));

        Assert.HasCount(1, result);
        Assert.AreEqual(ChatTextStyle.None, result[0].Style);
        Assert.AreEqual(@"\#abcNormal\End\", result[0].Text);
    }

    [TestMethod]
    public void Parse_HexColorBoundary_HandlesInvalidChars()
    {
        var input = @"\#00FF0G\#00fF00Correct";
        var result = ChatText.Parse(input, new(0, 0, 0, 0));

        Assert.HasCount(2, result);
        Assert.AreEqual(@"\#00FF0G", result[0].Text);
        Assert.AreEqual("Correct", result[1].Text);
        Assert.AreEqual(new Color(0, 255, 0, 255), result[1].Color);
    }

    [TestMethod]
    public void Parse_RapidColorChanges_RespectsLastState()
    {
        var input = @"\1\2\rText";
        var result = ChatText.Parse(input, new(255, 0, 0, 0));

        Assert.HasCount(1, result);
        Assert.AreEqual("Text", result[0].Text);
        Assert.AreEqual(new(255, 0, 0, 0), result[0].Color);
    }

    // Helper
    private static void AssertSegment(
        ChatTextSegment segment,
        ChatTextStyle expectedStyle,
        Color expectedColor,
        string expectedText)
    {
        Assert.AreEqual(expectedStyle, segment.Style);
        Assert.AreEqual(expectedColor, segment.Color);
        Assert.AreEqual(expectedText, segment.Text);
    }
}
