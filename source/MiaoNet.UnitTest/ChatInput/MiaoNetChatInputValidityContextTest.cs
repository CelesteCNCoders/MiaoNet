using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class MiaoNetChatInputValidityContextTest
{
    private static readonly MiaoNetCommand CmdHelp = new("help", ["h"], [], false, null!);
    private static readonly CommandParser Parser = new([CmdHelp]);

    private sealed class FakeState : IChatInputState
    {
        public bool IsConnected { get; set; } = true;

        public int Version { get; set; }

        public IReadOnlyCollection<string> AllPlayerNames { get; set; } = [];

        public IReadOnlyCollection<string> PublicChannelNames { get; set; } = [];

        public Dictionary<CommandSegmentType, IReadOnlyCollection<string>> PlayersInScope { get; } = [];

        public IReadOnlyCollection<string> PlayerNamesInScope(CommandSegmentType scope)
            => PlayersInScope.TryGetValue(scope, out var names) ? names : [];
    }

    private static MiaoNetChatInputValidityContext Create(
        FakeState state, bool channelType = false, bool emoji = false
    )
        => new(state, Parser, _ => channelType, _ => emoji);

    [TestMethod]
    public void NotConnected_PlayerChannelAndMentionNeutral()
    {
        var state = new FakeState { IsConnected = false };
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Neutral, ctx.CheckPlayer("Alice", CommandSegmentType.Player));
        Assert.AreEqual(ChatInputValidity.Neutral, ctx.CheckChannel("main"));
        Assert.AreEqual(ChatInputValidity.Neutral, ctx.CheckMention("Alice"));
    }

    [TestMethod]
    public void PlayerMatch_IsCaseSensitive()
    {
        var state = new FakeState();
        state.PlayersInScope[CommandSegmentType.Player] = ["Alice"];
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckPlayer("Alice", CommandSegmentType.Player));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckPlayer("alice", CommandSegmentType.Player));
    }

    [TestMethod]
    public void PlayerMatch_UsesRequestedScope()
    {
        var state = new FakeState();
        state.PlayersInScope[CommandSegmentType.PlayerSameMap] = ["Bob"];
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckPlayer("Bob", CommandSegmentType.PlayerSameMap));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckPlayer("Bob", CommandSegmentType.Player));
    }

    [TestMethod]
    public void ChannelMatch_IsCaseInsensitive()
    {
        var state = new FakeState { PublicChannelNames = ["Main"] };
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckChannel("main"));
        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckChannel("MAIN"));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckChannel("other"));
    }

    [TestMethod]
    public void CommandMatch_UsesParserCommandsAndAliases()
    {
        var ctx = Create(new FakeState());

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckCommand("help"));
        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckCommand("H"));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckCommand("nope"));
    }

    [TestMethod]
    public void ChannelTypeAndEmoji_DelegateToInjectedChecks()
    {
        var ctx = Create(new FakeState(), channelType: true, emoji: true);
        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckChannelType("global"));
        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckEmoji("joy"));

        var none = Create(new FakeState());
        Assert.AreEqual(ChatInputValidity.Invalid, none.CheckChannelType("global"));
        Assert.AreEqual(ChatInputValidity.Invalid, none.CheckEmoji("joy"));
    }

    [TestMethod]
    public void MentionMatch_UsesRosterCaseSensitively()
    {
        var state = new FakeState { AllPlayerNames = ["Alice", "Bobby"] };
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckMention("Bobby"));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckMention("bobby"));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckMention("nobody"));
    }
}
