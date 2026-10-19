using System.Collections.Immutable;
using Celeste.Mod.ChatInputBox;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class ChatMentionParserTest
{
    private static readonly Color DefaultColor = new(255, 255, 255);
    private static readonly Color MentionColor = new(1, 2, 3);

    private static (bool MentionsSelf, ImmutableArray<ChatTextSegment> Segments) Split(
        string text, string? selfName, params string[] names
    )
    {
        var builder = ImmutableArray.CreateBuilder<ChatTextSegment>();
        var segment = new ChatTextSegment(DefaultColor, text);
        bool mentionsSelf = ChatMentionParser.SplitMentionSegments(
            builder,
            segment,
            new HashSet<string>(names, StringComparer.Ordinal),
            selfName,
            MentionColor
        );
        return (mentionsSelf, builder.ToImmutable());
    }

    private static string[] Texts(ImmutableArray<ChatTextSegment> segments)
        => segments.Select(s => s.Text).ToArray();

    [TestMethod]
    public void WholeWordMention_IsColored_NotSelf()
    {
        var (mentionsSelf, segments) = Split("hi @bob there", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hi ", "@bob", " there" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
        Assert.AreEqual(MentionColor, segments[1].Color);
        Assert.AreEqual(DefaultColor, segments[2].Color);
    }

    [TestMethod]
    public void SelfMention_ReportsMentionsSelf()
    {
        var (mentionsSelf, segments) = Split("hello @bob", "bob", "bob");

        Assert.IsTrue(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hello ", "@bob" }, Texts(segments));
        Assert.AreEqual(MentionColor, segments[^1].Color);
    }

    [TestMethod]
    public void MentionAtStartOfText_Matches()
    {
        var (mentionsSelf, segments) = Split("@bob hi", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "@bob", " hi" }, Texts(segments));
        Assert.AreEqual(MentionColor, segments[0].Color);
    }

    [TestMethod]
    public void TrailingPunctuation_IsNotAMention()
    {
        var (mentionsSelf, segments) = Split("thanks @bob!", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "thanks @bob!" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
    }

    [TestMethod]
    public void Matching_IsCaseSensitive()
    {
        var lower = Split("@bob", null, "bob");
        Assert.AreEqual(MentionColor, lower.Segments[0].Color);

        var upper = Split("@Bob", null, "bob");
        Assert.IsFalse(upper.MentionsSelf);
        CollectionAssert.AreEqual(new[] { "@Bob" }, Texts(upper.Segments));
        Assert.AreEqual(DefaultColor, upper.Segments[0].Color);
    }

    [TestMethod]
    public void AtNotAtWordStart_IsIgnored()
    {
        var (mentionsSelf, segments) = Split("hi@bob", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hi@bob" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
    }

    [TestMethod]
    public void LoneAt_IsIgnored()
    {
        var (mentionsSelf, segments) = Split("hi @ there", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hi @ there" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
    }

    [TestMethod]
    public void UnmatchedMention_PreservesWholeTextUncolored()
    {
        var (mentionsSelf, segments) = Split("hi @nobody there", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hi @nobody there" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
    }

    [TestMethod]
    public void MultipleMentions_AreAllColored_AndSelfIsReported()
    {
        var (mentionsSelf, segments) = Split("@bob @alice", "alice", "bob", "alice");

        Assert.IsTrue(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "@bob", " ", "@alice" }, Texts(segments));
        Assert.AreEqual(MentionColor, segments[0].Color);
        Assert.AreEqual(DefaultColor, segments[1].Color);
        Assert.AreEqual(MentionColor, segments[2].Color);
    }

    [TestMethod]
    public void NoAt_ReturnsSegmentUnchanged()
    {
        var (mentionsSelf, segments) = Split("hello world", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hello world" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
    }

    [TestMethod]
    public void NonAsciiWhitespace_DelimitsMention()
    {
        var (mentionsSelf, segments) = Split("hi\u00A0@bob\u00A0there", null, "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "hi\u00A0", "@bob", "\u00A0there" }, Texts(segments));
        Assert.AreEqual(MentionColor, segments[1].Color);
    }

    [TestMethod]
    public void NullSelfName_WithMatchingName_StillColored_NotSelf()
    {
        var (mentionsSelf, segments) = Split("@bob", null, "bob");

        Assert.IsFalse(mentionsSelf);
        Assert.AreEqual(MentionColor, segments[0].Color);
    }

    [TestMethod]
    public void EmptyNameSet_NoMentions()
    {
        var (mentionsSelf, segments) = Split("@bob hi", "bob");

        Assert.IsFalse(mentionsSelf);
        CollectionAssert.AreEqual(new[] { "@bob hi" }, Texts(segments));
        Assert.AreEqual(DefaultColor, segments[0].Color);
    }
}
