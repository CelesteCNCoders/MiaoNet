using Celeste.Mod.ChatInputBox;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class ChatInputHighlighterTest
{
    private static readonly MiaoNetCommand CmdHelp = new("help", ["h"], [], false, null!);
    private static readonly MiaoNetCommand CmdWhisper = new("whisper", ["w"], [CommandSegmentType.Player, CommandSegmentType.Text], true, null!);
    private static readonly MiaoNetCommand CmdChat = new("chat", ["c"], [CommandSegmentType.ChatChannelType], false, null!);
    private static readonly MiaoNetCommand CmdJoin = new("channel", ["join"], [CommandSegmentType.ChannelOrNew], true, null!);
    private static readonly MiaoNetCommand CmdExistingChannel = new("exchannel", null, [CommandSegmentType.Channel], false, null!);

    private static readonly IReadOnlyCollection<MiaoNetCommand> Commands = [CmdHelp, CmdWhisper, CmdChat, CmdJoin, CmdExistingChannel];

    private sealed class FakeValidity : IChatInputValidityContext
    {
        public ChatInputValidity Command = ChatInputValidity.Neutral;
        public IReadOnlySet<string>? CommandNames;
        public ChatInputValidity Player = ChatInputValidity.Neutral;
        public ChatInputValidity Channel = ChatInputValidity.Neutral;
        public ChatInputValidity ChannelType = ChatInputValidity.Neutral;
        public ChatInputValidity Emoji = ChatInputValidity.Neutral;
        public IReadOnlySet<string>? EmojiNames;
        public IReadOnlySet<string>? MentionNames;

        public ChatInputValidity CheckCommand(string name)
            => CommandNames is null
                ? Command
                : (CommandNames.Contains(name) ? ChatInputValidity.Valid : ChatInputValidity.Invalid);
        public ChatInputValidity CheckPlayer(string name, CommandSegmentType scope) => Player;
        public ChatInputValidity CheckMention(string name)
            => MentionNames is null
                ? ChatInputValidity.Neutral
                : (MentionNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) ? ChatInputValidity.Valid : ChatInputValidity.Invalid);
        public ChatInputValidity CheckChannel(string name) => Channel;
        public ChatInputValidity CheckChannelType(string name) => ChannelType;
        public ChatInputValidity CheckEmoji(string name)
            => EmojiNames is null
                ? Emoji
                : (EmojiNames.Contains(name) ? ChatInputValidity.Valid : ChatInputValidity.Invalid);
    }

    private sealed class FakeState : IChatInputState
    {
        public bool IsConnected => true;
        public int Version { get; set; }
        public IReadOnlyCollection<string> AllPlayerNames => [];
        public IReadOnlyCollection<string> PlayerNamesInScope(CommandSegmentType scope) => [];
        public IReadOnlyCollection<string> PublicChannelNames => [];
    }

    private static IReadOnlyList<ChatInputHighlightSpan> Highlight(FakeValidity validity, string text, int caret)
        => new ChatInputHighlighter(new CommandParser(Commands), validity, new FakeState()).Highlight(text, caret);

    private static Color ColorAt(IReadOnlyList<ChatInputHighlightSpan> spans, int index)
        => spans.First(s => index >= s.Start && index < s.End).Color;

    [TestMethod]
    public void SpansCoverWholeText()
    {
        var spans = Highlight(new FakeValidity(), "/w Alice :joy:", 14);
        Assert.AreEqual(14, spans.Sum(s => s.Length));
    }

    [TestMethod]
    public void ValidCommand_Green()
    {
        var validity = new FakeValidity { Command = ChatInputValidity.Valid };
        Assert.AreEqual(ChatInputHighlightColors.Command, ColorAt(Highlight(validity, "/help ", 6), 1));
    }

    [TestMethod]
    public void InvalidCommand_Delimited_Red()
    {
        var validity = new FakeValidity { Command = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "/unknown ", 9), 1));
    }

    [TestMethod]
    public void InvalidCommand_StillTyping_NotRed()
    {
        var validity = new FakeValidity { Command = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "/unknown", 8), 1));
    }

    [TestMethod]
    public void InvalidCommand_CaretInside_NotRed()
    {
        var validity = new FakeValidity { Command = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "/unknown", 4), 1));
    }

    [TestMethod]
    public void NeutralCommand_White()
    {
        var validity = new FakeValidity { Command = ChatInputValidity.Neutral };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "/help ", 6), 1));
    }

    [TestMethod]
    public void CommandName_CheckedWithoutPrefix()
    {
        var validity = new FakeValidity { CommandNames = new HashSet<string> { "help", "h" } };

        Assert.AreEqual(ChatInputHighlightColors.Command, ColorAt(Highlight(validity, "/help ", 6), 1));
        Assert.AreEqual(ChatInputHighlightColors.Command, ColorAt(Highlight(validity, "/h ", 3), 1));
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "/nope ", 6), 1));
    }

    [TestMethod]
    public void Mention_ResolvedGold_UnresolvedRed()
    {
        var valid = new FakeValidity { MentionNames = new HashSet<string> { "bob" } };
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(Highlight(valid, "@bob ", 5), 1));

        var invalid = new FakeValidity { MentionNames = new HashSet<string>() };
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(invalid, "@bob ", 5), 1));
    }

    [TestMethod]
    public void Mention_StillTyping_NotRed()
    {
        var invalid = new FakeValidity { MentionNames = new HashSet<string>() };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(invalid, "@bo", 3), 1));
    }

    [TestMethod]
    public void LoneAt_NotHighlighted()
    {
        var validity = new FakeValidity { MentionNames = new HashSet<string> { "bob" } };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "@ ", 2), 0));
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "hi @ there", 10), 3));
    }

    [TestMethod]
    public void Mention_NoRoster_NeutralEvenWhenDelimited()
    {
        var validity = new FakeValidity { MentionNames = null };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "@bob ", 5), 1));
    }

    [TestMethod]
    public void Mention_Resolved_ThenPlainText()
    {
        var validity = new FakeValidity { MentionNames = new HashSet<string> { "bob" } };
        var spans = Highlight(validity, "@bob hi", 7);

        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(spans, 1));
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(spans, 5));
    }

    [TestMethod]
    public void Mention_Resolved_CaseInsensitive()
    {
        var validity = new FakeValidity { MentionNames = new HashSet<string> { "bob" } };
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(Highlight(validity, "@Bob ", 5), 1));
    }

    [TestMethod]
    public void Mention_Resolved_ThenEmoji()
    {
        var validity = new FakeValidity
        {
            MentionNames = new HashSet<string> { "bob" },
            EmojiNames = new HashSet<string> { "glad" }
        };
        var spans = Highlight(validity, "hi @bob :glad:", 14);

        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(spans, 4));
        Assert.AreEqual(ChatInputHighlightColors.Emoji, ColorAt(spans, 9));
    }

    [TestMethod]
    public void Mention_IsTheWholeWord()
    {
        var validity = new FakeValidity
        {
            MentionNames = new HashSet<string> { "bob", "a:joy", ":sad:" }
        };

        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(Highlight(validity, "@a:joy ", 7), 1));
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(Highlight(validity, "@:sad: ", 7), 1));
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "@bob:glad: ", 11), 1));
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "@bob:glad:", 10), 1));
    }

    [TestMethod]
    public void Mention_Unresolved_RedOnlyWhenWhitespaceDelimited()
    {
        var validity = new FakeValidity { MentionNames = new HashSet<string>() };

        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "@bbb,", 5), 1));
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "@bbb, ", 6), 1));
    }

    [TestMethod]
    public void Mention_Unresolved_EmojiStillColored()
    {
        var validity = new FakeValidity
        {
            MentionNames = new HashSet<string>(),
            EmojiNames = new HashSet<string> { "glad" }
        };

        var spans = Highlight(validity, "@:glad:a", 8);
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(spans, 0));
        Assert.AreEqual(ChatInputHighlightColors.Emoji, ColorAt(spans, 3));
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(spans, 7));

        spans = Highlight(validity, "@:glad:a ", 9);
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(spans, 0));
        Assert.AreEqual(ChatInputHighlightColors.Emoji, ColorAt(spans, 3));
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(spans, 7));
    }

    [TestMethod]
    public void Mention_Resolved_EmojiIsPlayerColor()
    {
        var validity = new FakeValidity
        {
            MentionNames = new HashSet<string> { ":sad:" },
            EmojiNames = new HashSet<string> { "sad" }
        };
        var spans = Highlight(validity, "@:sad: ", 7);
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(spans, 0));
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(spans, 3));
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(spans, 5));
    }

    [TestMethod]
    public void PlayerArgument_ValidGold_InvalidRed()
    {
        var valid = new FakeValidity { Player = ChatInputValidity.Valid };
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(Highlight(valid, "/w Alice ", 9), 4));

        var invalid = new FakeValidity { Player = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(invalid, "/w Alice ", 9), 4));
    }

    [TestMethod]
    public void UnconnectedPlayer_NeutralWhite()
    {
        var validity = new FakeValidity { Player = ChatInputValidity.Neutral };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "/w Alice ", 9), 4));
    }

    [TestMethod]
    public void ChannelTypeArgument_InvalidRed()
    {
        var validity = new FakeValidity { ChannelType = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "/chat xx ", 9), 7));
    }

    [TestMethod]
    public void ChannelOrNewArgument_ExistingCyan()
    {
        var validity = new FakeValidity { Channel = ChatInputValidity.Valid };
        Assert.AreEqual(ChatInputHighlightColors.Channel, ColorAt(Highlight(validity, "/channel foo ", 13), 10));
        Assert.AreEqual(ChatInputHighlightColors.Channel, ColorAt(Highlight(validity, "/join main", 10), 7));
    }

    [TestMethod]
    public void ChannelOrNewArgument_Missing_PurpleEvenWhileTyping()
    {
        var validity = new FakeValidity { Channel = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.NewChannel, ColorAt(Highlight(validity, "/channel foo ", 13), 10));
        Assert.AreEqual(ChatInputHighlightColors.NewChannel, ColorAt(Highlight(validity, "/join mai", 9), 7));
    }

    [TestMethod]
    public void ChannelArgument_PrivatePrefix_AlwaysPrivateColor()
    {
        var validity = new FakeValidity { Channel = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.PrivateChannel, ColorAt(Highlight(validity, "/join !foo", 10), 7));
        Assert.AreEqual(ChatInputHighlightColors.PrivateChannel, ColorAt(Highlight(validity, "/join !fo", 9), 7));
    }

    [TestMethod]
    public void ChannelName_WithWhitespace_Red()
    {
        var validity = new FakeValidity { Channel = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "/channel foo bar", 16), 10));
    }

    [TestMethod]
    public void ExistingChannelArgument_Missing_RedWhenDelimited_WhiteWhileTyping()
    {
        var validity = new FakeValidity { Channel = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(Highlight(validity, "/exchannel foo ", 15), 12));
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, "/exchannel fo", 13), 12));
    }

    [TestMethod]
    public void Emoji_HighlightedOnlyWhenRegistered()
    {
        var validity = new FakeValidity { EmojiNames = new HashSet<string> { "glad" } };

        Assert.AreEqual(ChatInputHighlightColors.Emoji, ColorAt(Highlight(validity, ":glad:", 6), 1));
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(validity, ":joy:", 5), 1));
    }

    [TestMethod]
    public void UnmatchedColonThenEmoji_OnlyEmojiColored()
    {
        var validity = new FakeValidity { EmojiNames = new HashSet<string> { "glad" } };
        var spans = Highlight(validity, ":invalid:glad:", 14);

        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(spans, 1));
        Assert.AreEqual(ChatInputHighlightColors.Emoji, ColorAt(spans, 10));
    }

    [TestMethod]
    public void ColonText_NotHighlighted()
    {
        var validity = new FakeValidity { EmojiNames = new HashSet<string> { "glad" } };
        var spans = Highlight(validity, "time 11:22:33", 13);
        Assert.IsTrue(spans.All(s => s.Color == ChatInputHighlightColors.Text));
    }

    [TestMethod]
    public void UnclosedEmoji_NotRed()
    {
        var invalid = new FakeValidity { Emoji = ChatInputValidity.Invalid };
        Assert.AreEqual(ChatInputHighlightColors.Text, ColorAt(Highlight(invalid, ":jo", 3), 1));
    }

    [TestMethod]
    public void StateVersionChange_ReevaluatesSameInput()
    {
        var validity = new FakeValidity { Player = ChatInputValidity.Invalid };
        var state = new FakeState();
        var highlighter = new ChatInputHighlighter(new CommandParser(Commands), validity, state);

        Assert.AreEqual(ChatInputHighlightColors.Invalid, ColorAt(highlighter.Highlight("/w Alice ", 9), 4));

        validity.Player = ChatInputValidity.Valid;
        state.Version++;
        Assert.AreEqual(ChatInputHighlightColors.Player, ColorAt(highlighter.Highlight("/w Alice ", 9), 4));
    }
}
