using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class ChatInputTokenizerTest
{
    private static readonly MiaoNetCommand CmdHelp = new("help", ["h"], [], false, null!);
    private static readonly MiaoNetCommand CmdSay = new("say", null, [CommandSegmentType.Text], true, null!);
    private static readonly MiaoNetCommand CmdWhisper = new("whisper", ["w"], [CommandSegmentType.Player, CommandSegmentType.Text], true, null!);
    private static readonly MiaoNetCommand CmdChat = new("chat", ["c"], [CommandSegmentType.ChatChannelType], false, null!);
    private static readonly MiaoNetCommand CmdJoin = new("channel", ["join"], [CommandSegmentType.ChannelOrNew], true, null!);

    private static readonly IReadOnlyCollection<MiaoNetCommand> Commands = [CmdHelp, CmdSay, CmdWhisper, CmdChat, CmdJoin];

    private static readonly CommandParser Parser = new(Commands);

    private static readonly Func<string, bool> IsEmoji = name => name is "joy" or "glad";

    private static ChatInputToken[] Tokenize(string text)
        => ChatInputTokenizer.Tokenize(text, Parser, IsEmoji).ToArray();

    [TestMethod]
    public void EmptyText_NoTokens()
        => Assert.IsEmpty(Tokenize(""));

    [TestMethod]
    public void CommandName_Only()
    {
        var tokens = Tokenize("/help");
        Assert.HasCount(1, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 5);
    }

    [TestMethod]
    public void UnknownCommandName_StillTokenized()
    {
        var tokens = Tokenize("/unknown");
        Assert.HasCount(1, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 8);
    }

    [TestMethod]
    public void SayCommand_RecognizesMentionAndEmojiInTextArgument()
    {
        var tokens = Tokenize("/say hello @bob :joy:");

        Assert.HasCount(5, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 4);
        AssertToken(tokens[1], ChatInputTokenType.Text, 5, 6);
        AssertToken(tokens[2], ChatInputTokenType.Mention, 11, 4);
        AssertToken(tokens[3], ChatInputTokenType.Text, 15, 1);
        AssertToken(tokens[4], ChatInputTokenType.Emoji, 16, 5);
    }

    [TestMethod]
    public void WhisperCommand_TypedPlayerArgumentThenRest()
    {
        var tokens = Tokenize("/w Alice hi");

        Assert.HasCount(3, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 2);
        AssertToken(tokens[1], ChatInputTokenType.Argument, 3, 5);
        Assert.AreEqual(CommandSegmentType.Player, tokens[1].SegmentType);
        AssertToken(tokens[2], ChatInputTokenType.Text, 9, 2);
    }

    [TestMethod]
    public void WhisperCommand_NonBreakingSpaceSeparatesNameAndArguments()
    {
        var tokens = Tokenize("/w\u00A0Alice hi");

        Assert.HasCount(3, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 2);
        AssertToken(tokens[1], ChatInputTokenType.Argument, 3, 5);
        Assert.AreEqual(CommandSegmentType.Player, tokens[1].SegmentType);
        AssertToken(tokens[2], ChatInputTokenType.Text, 9, 2);
    }

    [TestMethod]
    public void ChatCommand_ChannelTypeArgument()
    {
        var tokens = Tokenize("/chat global");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 5);
        AssertToken(tokens[1], ChatInputTokenType.Argument, 6, 6);
        Assert.AreEqual(CommandSegmentType.ChatChannelType, tokens[1].SegmentType);
    }

    [TestMethod]
    public void TooManyArguments_FallBackToText()
    {
        var tokens = Tokenize("/chat global extra");

        Assert.HasCount(3, tokens);
        Assert.AreEqual(CommandSegmentType.ChatChannelType, tokens[1].SegmentType);
        AssertToken(tokens[2], ChatInputTokenType.Text, 13, 5);
    }

    [TestMethod]
    public void ChannelArgument_ExcludesTrailingSpace()
    {
        var tokens = Tokenize("/channel foo ");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 8);
        AssertToken(tokens[1], ChatInputTokenType.Argument, 9, 3);
        Assert.AreEqual(CommandSegmentType.ChannelOrNew, tokens[1].SegmentType);
    }

    [TestMethod]
    public void ChannelArgument_CapturesRestIncludingSpaces()
    {
        var tokens = Tokenize("/channel foo bar");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 8);
        AssertToken(tokens[1], ChatInputTokenType.Argument, 9, 7);
        Assert.AreEqual(CommandSegmentType.ChannelOrNew, tokens[1].SegmentType);
    }

    [TestMethod]
    public void TextRestArgument_KeepsInteriorSpaces()
    {
        var tokens = Tokenize("/w Alice hi there");

        Assert.HasCount(3, tokens);
        AssertToken(tokens[0], ChatInputTokenType.CommandName, 0, 2);
        AssertToken(tokens[1], ChatInputTokenType.Argument, 3, 5);
        AssertToken(tokens[2], ChatInputTokenType.Text, 9, 8);
    }

    [TestMethod]
    public void NonCommandText_RecognizesMention()
    {
        var tokens = Tokenize("hi @bob");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 3);
        AssertToken(tokens[1], ChatInputTokenType.Mention, 3, 4);
    }

    [TestMethod]
    public void Mention_AbsorbsTrailingEmoji()
    {
        var tokens = Tokenize("hi @bob:glad:");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 3);
        AssertToken(tokens[1], ChatInputTokenType.Mention, 3, 10);
    }

    [TestMethod]
    public void Mention_SpansWholeWhitespaceDelimitedWord()
    {
        var tokens = Tokenize("@bob, hi");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Mention, 0, 5);
        AssertToken(tokens[1], ChatInputTokenType.Text, 5, 3);
    }

    [TestMethod]
    public void LoneAt_StaysText()
    {
        var tokens = Tokenize("hi @ there");

        Assert.HasCount(1, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 10);
    }

    [TestMethod]
    public void ColonTime_StaysText()
    {
        var tokens = Tokenize("11:22:33");
        Assert.HasCount(1, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 8);
    }

    [TestMethod]
    public void UnmatchedColons_ThenRegisteredEmoji()
    {
        var tokens = Tokenize(":invalid:glad:");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 8);
        AssertToken(tokens[1], ChatInputTokenType.Emoji, 8, 6);
    }

    [TestMethod]
    public void RegisteredEmoji_ThenLiteralTrailingColon()
    {
        var tokens = Tokenize(":glad:glad:");

        Assert.HasCount(2, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Emoji, 0, 6);
        AssertToken(tokens[1], ChatInputTokenType.Text, 6, 5);
    }

    [TestMethod]
    public void UnclosedEmoji_StaysText()
    {
        var tokens = Tokenize(":jo");
        Assert.HasCount(1, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 3);
    }

    [TestMethod]
    public void LoneColon_StaysText()
    {
        var tokens = Tokenize(":");
        Assert.HasCount(1, tokens);
        AssertToken(tokens[0], ChatInputTokenType.Text, 0, 1);
    }

    private static void AssertToken(ChatInputToken token, ChatInputTokenType type, int start, int length)
    {
        Assert.AreEqual(type, token.Type);
        Assert.AreEqual(start, token.Start);
        Assert.AreEqual(length, token.Length);
    }
}
