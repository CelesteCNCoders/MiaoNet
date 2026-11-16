using System.Collections.Immutable;
using Celeste.Mod.ChatInputBox;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class MentionBoundaryTest
{
    private static readonly Color DefaultColor = new(255, 255, 255);
    private static readonly Color MentionColor = new(1, 2, 3);

    private static readonly MiaoNetCommand CmdSay =
        new("say", null, [CommandSegmentType.Text], true, null!);

    private static readonly CommandParser Parser = new([CmdSay]);

    private static (int Start, int Length)[] LexerMentionSpans(string text)
        => ChatLexer.Lex(text, Parser.Commands, static _ => false).Tokens
            .Where(t => t.Type == ChatInputTokenType.Mention)
            .Select(t => (t.Start, t.Length))
            .ToArray();

    private static (int Start, int Length)[] ParserMentionSpans(string text, params string[] names)
    {
        var builder = ImmutableArray.CreateBuilder<ChatTextSegment>();
        ChatMentionParser.SplitMentionSegments(
            builder,
            new ChatTextSegment(DefaultColor, text),
            new HashSet<string>(names, StringComparer.Ordinal),
            null,
            MentionColor);

        List<(int Start, int Length)> spans = [];
        int cursor = 0;
        foreach (ChatTextSegment segment in builder)
        {
            if (segment.Color == MentionColor)
                spans.Add((cursor, segment.Text.Length));
            cursor += segment.Text.Length;
        }
        return spans.ToArray();
    }

    [TestMethod]
    public void SameSpans_ApartFromNamesTheParserDoesNotKnow()
    {
        // Each case is (text, the name the parser roster contains).
        var cases = new[]
        {
            ("hi @bob there", "bob"),
            ("@bob hi", "bob"),
            ("@bob:glad: hi", "bob:glad:"),
            ("hi@bob", "bob"),
            ("hi@bob there", "bob"),
            ("hi @ there", "bob"),
            ("x @bob @alice y", "bob|alice"),
        };

        foreach ((string text, string known) in cases)
        {
            // "|" separates several roster names; the lexer never needs a roster.
            CollectionAssert.AreEqual(
                LexerMentionSpans(text),
                ParserMentionSpans(text, known.Split('|')),
                $"lexer and mention parser disagree on: {text}");
        }
    }

    [TestMethod]
    public void EmojiBearingWord_IsOneMention_NotTheShorterName()
    {
        CollectionAssert.AreEqual(new[] { (3, 10) }, LexerMentionSpans("hi @bob:glad:"));

        Assert.IsEmpty(ParserMentionSpans("hi @bob:glad:", "bob"));
        CollectionAssert.AreEqual(new[] { (3, 10) }, ParserMentionSpans("hi @bob:glad:", "bob:glad:"));
    }

    [TestMethod]
    public void MentionNeedsNoSeparatorBeforeTheAtSign()
    {
        CollectionAssert.AreEqual(new[] { (0, 4) }, LexerMentionSpans("@bob hi"));
        CollectionAssert.AreEqual(new[] { (2, 4) }, LexerMentionSpans("hi@bob there"));
        CollectionAssert.AreEqual(new[] { (1, 4) }, LexerMentionSpans("(@bob there"));

        CollectionAssert.AreEqual(new[] { (0, 4) }, ParserMentionSpans("@bob hi", "bob"));
        CollectionAssert.AreEqual(new[] { (2, 4) }, ParserMentionSpans("hi@bob there", "bob"));
    }

    [TestMethod]
    public void MentionStillEndsAtWhitespace()
    {
        CollectionAssert.AreEqual(new[] { (3, 5) }, LexerMentionSpans("hi @bob,"));
        CollectionAssert.AreEqual(new[] { (3, 5) }, ParserMentionSpans("hi @bob,", "bob,"));
    }

    [TestMethod]
    public void FirstAtClaimsTheWholeWord()
    {
        CollectionAssert.AreEqual(new[] { (3, 4) }, LexerMentionSpans("hi @a@b"));

        Assert.IsEmpty(ParserMentionSpans("hi @a@b", "b"));
        CollectionAssert.AreEqual(new[] { (3, 4) }, ParserMentionSpans("hi @a@b", "a@b"));
    }
}
