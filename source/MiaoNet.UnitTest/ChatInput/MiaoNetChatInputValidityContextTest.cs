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

        public Dictionary<PlayerScope, IReadOnlyCollection<string>> PlayersInScope { get; } = [];

        public IReadOnlyCollection<string> PlayerNamesInScope(PlayerScope scope)
            => PlayersInScope.TryGetValue(scope, out var names) ? names : [];
    }

    private static MiaoNetChatInputValidityContext Create(
        FakeState state, bool channelType = false, bool emoji = false
    )
        => new(state, Parser, _ => channelType, ["global", "channel", "map"], _ => emoji);

    [TestMethod]
    public void NotConnected_PlayerChannelAndMentionNeutral()
    {
        var state = new FakeState { IsConnected = false };
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Neutral, ctx.CheckPlayer("Alice", PlayerScope.Any));
        Assert.AreEqual(ChatInputValidity.Neutral, ctx.CheckChannel("main"));
        Assert.AreEqual(ChatInputValidity.Neutral, ctx.CheckMention("Alice"));
    }

    [TestMethod]
    public void PlayerMatch_IsCaseSensitive()
    {
        var state = new FakeState();
        state.PlayersInScope[PlayerScope.Any] = ["Alice"];
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckPlayer("Alice", PlayerScope.Any));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckPlayer("alice", PlayerScope.Any));
    }

    [TestMethod]
    public void PlayerMatch_UsesRequestedScope()
    {
        var state = new FakeState();
        state.PlayersInScope[PlayerScope.SameMap] = ["Bob"];
        var ctx = Create(state);

        Assert.AreEqual(ChatInputValidity.Valid, ctx.CheckPlayer("Bob", PlayerScope.SameMap));
        Assert.AreEqual(ChatInputValidity.Invalid, ctx.CheckPlayer("Bob", PlayerScope.Any));
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

    [TestMethod]
    public void CanStillCompleteCommand_MatchesNameAndAliases()
    {
        var ctx = Create(new FakeState());

        Assert.IsTrue(ctx.CanStillCompleteCommand(""));
        Assert.IsTrue(ctx.CanStillCompleteCommand("he"));
        Assert.IsTrue(ctx.CanStillCompleteCommand("H"));
        Assert.IsFalse(ctx.CanStillCompleteCommand("zzz"));
    }

    [TestMethod]
    public void CanStillCompletePlayer_IsCaseInsensitive()
    {
        var state = new FakeState();
        state.PlayersInScope[PlayerScope.Any] = ["Alice"];
        var ctx = Create(state);

        Assert.IsTrue(ctx.CanStillCompletePlayer("ali", PlayerScope.Any));
        Assert.IsTrue(ctx.CanStillCompletePlayer("ICE", PlayerScope.Any));
        Assert.IsFalse(ctx.CanStillCompletePlayer("bob", PlayerScope.Any));
    }

    [TestMethod]
    public void CanStillCompleteMention_UsesWholeRoster()
    {
        var state = new FakeState { AllPlayerNames = ["Alice", "Bobby"] };
        var ctx = Create(state);

        Assert.IsTrue(ctx.CanStillCompleteMention("bob"));
        Assert.IsFalse(ctx.CanStillCompleteMention("zed"));
    }

    [TestMethod]
    public void CanStillCompleteChannel_UsesPublicChannels()
    {
        var state = new FakeState { PublicChannelNames = ["Main"] };
        var ctx = Create(state);

        Assert.IsTrue(ctx.CanStillCompleteChannel("mai"));
        Assert.IsFalse(ctx.CanStillCompleteChannel("other"));
    }

    [TestMethod]
    public void CanStillCompleteChannelType_UsesKnownNames()
    {
        var ctx = Create(new FakeState());

        Assert.IsTrue(ctx.CanStillCompleteChannelType("gl"));
        Assert.IsFalse(ctx.CanStillCompleteChannelType("xyz"));
    }

    [TestMethod]
    public void CanStillComplete_IsAlwaysTrueWhenNotConnected()
    {
        var state = new FakeState { IsConnected = false };
        var ctx = Create(state);

        Assert.IsTrue(ctx.CanStillCompletePlayer("nobody", PlayerScope.Any));
        Assert.IsTrue(ctx.CanStillCompleteChannel("nothing"));
    }
}
