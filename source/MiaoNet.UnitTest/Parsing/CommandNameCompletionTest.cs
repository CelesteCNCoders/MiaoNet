using Celeste.Mod.ChatInputBox;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class CommandNameCompletionTest
{
    // Declaration order matters for ties.
    private static readonly MiaoNetCommand CmdChannel = new("channel", ["join"], [], false, null!);
    private static readonly MiaoNetCommand CmdWhisper = new("whisper", ["w", "msg"], [], false, null!);
    private static readonly MiaoNetCommand CmdWatch = new("watch", ["wt"], [], false, null!);
    private static readonly MiaoNetCommand CmdAz = new("az", ["za"], [], false, null!);
    private static readonly MiaoNetCommand CmdAbc = new("abc", ["abd", "abe"], [], false, null!);
    private static readonly MiaoNetCommand CmdPing = new("ping", null, [], false, null!);
    private static readonly MiaoNetCommand CmdAtlas = new("atlas", null, [], false, null!);
    private static readonly MiaoNetCommand CmdXray = new("xray", ["yo"], [], false, null!);
    private static readonly MiaoNetCommand CmdYellow = new("yellow", null, [], false, null!);

    private static readonly IReadOnlyCollection<MiaoNetCommand> Fixture =
    [
        CmdChannel, CmdWhisper, CmdWatch, CmdAz, CmdAbc,
        CmdPing, CmdAtlas, CmdXray, CmdYellow
    ];

    private static (string Display, string Content)[] Complete(string fragment)
        => CommandNameCompletion.Complete(Fixture, fragment)
            .Select(c => (c.Display, c.Content))
            .ToArray();

    private static void AssertCompletions(string fragment, params (string Display, string Content)[] expected)
        => CollectionAssert.AreEqual(expected, Complete(fragment));

    [TestMethod]
    public void NamePrefix_ShowsNoAlias()
        => AssertCompletions("chan", ("channel", "channel"));

    [TestMethod]
    public void NamePrefix_MultiChar_ShowsNoAlias()
        => AssertCompletions("whis", ("whisper", "whisper"));

    [TestMethod]
    public void AliasPrefix_InsertsAlias_ShowsIt()
        => AssertCompletions("jo", ("channel (join)", "join"));

    [TestMethod]
    public void SecondAliasPrefix_InsertsAlias_ShowsIt()
        => AssertCompletions("ms", ("whisper (msg)", "msg"));

    [TestMethod]
    public void SingleChar_OrdersByQualityThenKindThenDeclaration()
        => AssertCompletions(
            "w",
            ("whisper (w)", "whisper"),
            ("watch (wt)", "watch"),
            ("yellow", "yellow"));

    [TestMethod]
    public void PrefixAliasBeatsInfixName()
        => AssertCompletions("z", ("az (za)", "za"));

    [TestMethod]
    public void MultiplePrefixAliases_NameWins()
        => AssertCompletions("ab", ("abc (abd, abe)", "abc"));

    [TestMethod]
    public void PrefixBeatsInfix()
        => AssertCompletions("at", ("atlas", "atlas"), ("watch", "watch"));

    [TestMethod]
    public void NameBeforeAlias_AtSameQuality()
        => AssertCompletions("y", ("yellow", "yellow"), ("xray (yo)", "yo"));

    [TestMethod]
    public void Matching_IsCaseInsensitive()
        => AssertCompletions(
            "W",
            ("whisper (w)", "whisper"),
            ("watch (wt)", "watch"),
            ("yellow", "yellow"));

    [TestMethod]
    public void NoMatch_IsEmpty()
        => AssertCompletions("nope");

    [TestMethod]
    public void EmptyFragment_ListsEveryCommandByName()
        => AssertCompletions(
            "",
            ("channel", "channel"),
            ("whisper", "whisper"),
            ("watch", "watch"),
            ("az", "az"),
            ("abc", "abc"),
            ("ping", "ping"),
            ("atlas", "atlas"),
            ("xray", "xray"),
            ("yellow", "yellow"));

    [TestMethod]
    public void InfixAliasOnly_ShownInDisplay()
    {
        // "msg" matches whisper's alias in the middle, and whisper's name not at all.
        var completions = Complete("sg");
        CollectionAssert.AreEqual(new[] { ("whisper (msg)", "msg") }, completions);
    }

    [TestMethod]
    public void InfixAliases_AreShownInDisplay()
    {
        // "/w": the name "unwatch" and both aliases match in the middle.
        var unwatch = new MiaoNetCommand("unwatch", ["uw", "uwt"], [], false, null!);
        var completions = CommandNameCompletion.Complete(new[] { unwatch }, "w")
            .Select(c => (c.Display, c.Content))
            .ToArray();
        CollectionAssert.AreEqual(new[] { ("unwatch (uw, uwt)", "unwatch") }, completions);
    }

    private static string Accept(string text, int remove, string content)
    {
        var buffer = new TextBuffer("/" + text);
        buffer.DoCompletion(remove, content, ' ', maxTextLength: 64);
        return buffer.Text;
    }

    [TestMethod]
    public void Accept_InsertsAliasSpelling_WithSeparator()
        => Assert.AreEqual("/join ", Accept("jo", 2, "join"));

    [TestMethod]
    public void Accept_InsertsNameSpelling()
        => Assert.AreEqual("/whisper ", Accept("w", 1, "whisper"));

    [TestMethod]
    public void Accept_SecondAliasSpelling()
        => Assert.AreEqual("/msg ", Accept("ms", 2, "msg"));

    [TestMethod]
    public void Accept_NamePrefix()
        => Assert.AreEqual("/channel ", Accept("chan", 4, "channel"));

    [TestMethod]
    public void Accept_MultiplePrefixAliases_NameContent()
        => Assert.AreEqual("/abc ", Accept("ab", 2, "abc"));

    [TestMethod]
    public void Accept_PrefixAliasBeatsInfixName()
        => Assert.AreEqual("/za ", Accept("z", 1, "za"));

    [TestMethod]
    public void Accept_PrefixBeforeInfix()
        => Assert.AreEqual("/atlas ", Accept("at", 2, "atlas"));

    [TestMethod]
    public void Accept_PlacesCaretAfterSeparator()
    {
        var buffer = new TextBuffer("/jo");
        buffer.DoCompletion(2, "join", ' ', maxTextLength: 64);

        Assert.AreEqual("/join ", buffer.Text);
        Assert.AreEqual(buffer.Text.Length, buffer.CaretPosition);
    }
}
