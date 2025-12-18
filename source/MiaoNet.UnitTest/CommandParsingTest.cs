#pragma warning disable CA1861
#pragma warning disable CA1825
#pragma warning disable IDE0044

using System.Collections;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class CommandParsingTest
{
    private static CommandParser parser = null!;
    private static MiaoNetCommand.Segment seg = new(CommandSegmentType.Text, null!, null);

    private static MiaoNetCommand cmdSay
        = new("say", null, null, [seg], true, null!);
    private static MiaoNetCommand cmdTest
        = new("test", null, null, [seg, seg], false, null!);
    private static MiaoNetCommand cmdPing
        = new("ping", null, null, [], false, null!);
    private static MiaoNetCommand cmdTp
        = new("tp", null, null, [seg], false, null!);
    private static MiaoNetCommand cmdBack
        = new("back", null, null, [seg], false, null!);
    private static MiaoNetCommand cmdWhisper
        = new("whisper", null, ["w", "msg"], [seg, seg], true, null!);

    [ClassInitialize]
    public static void SetUp(TestContext context)
    {
        parser = new([cmdSay, cmdTest, cmdPing, cmdTp, cmdBack, cmdWhisper]);
    }

    [TestMethod]
    public void Parse_ValidSayCommand_CapturesRest()
    {
        var result = parser.Parse("/say hello world", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("say", name);
        Assert.AreSame(cmdSay, cmd);
        CollectionAssert.AreEqual(new string[] { "hello world" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_ValidWhisperByAlias_CapturesRest()
    {
        var result = parser.Parse("/w Alice hi there!", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("w", name);
        Assert.AreSame(cmdWhisper, cmd);
        CollectionAssert.AreEqual(new string[] { "Alice", "hi there!" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_ValidWhisperByName_CapturesRest()
    {
        var result = parser.Parse("/whisper Bob   how are    you?", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("whisper", name);
        Assert.AreSame(cmdWhisper, cmd);
        CollectionAssert.AreEqual(new string[] { "Bob", "how are    you?" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_TestCommand_ExactTwoArgs()
    {
        var result = parser.Parse("/test foo bar", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("test", name);
        Assert.AreSame(cmdTest, cmd);
        CollectionAssert.AreEqual(new string[] { "foo", "bar" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_TestCommand_MissingArguments()
    {
        var result = parser.Parse("/test foo", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.MissingArguments, result);
        Assert.AreEqual("test", name);
        Assert.AreSame(cmdTest, cmd);
        CollectionAssert.AreEqual(new string[] { "foo" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_TestCommand_TooManyArguments()
    {
        var result = parser.Parse("/test foo bar baz", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.TooManyArguments, result);
        Assert.AreEqual("test", name);
        Assert.AreSame(cmdTest, cmd);
        CollectionAssert.AreEqual(new string[] { "foo", "bar", "baz" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_PingCommand_NoArgs()
    {
        var result = parser.Parse("/ping", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("ping", name);
        Assert.AreSame(cmdPing, cmd);
        CollectionAssert.AreEqual(new string[] { }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_TpCommand_OneArg()
    {
        var result = parser.Parse("/tp   spawn", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("tp", name);
        Assert.AreSame(cmdTp, cmd);
        CollectionAssert.AreEqual(new string[] { "spawn" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_TpCommand_TooManyArgs()
    {
        var result = parser.Parse("/tp here  please", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.TooManyArguments, result);
        Assert.AreEqual("tp", name);
        Assert.AreSame(cmdTp, cmd);
        CollectionAssert.AreEqual(new string[] { "here", "please" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_BackCommand_WithSpaceInArg_FailsBecauseNoCaptureRest()
    {
        var result = parser.Parse("/back somewhere safe", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.TooManyArguments, result);
        Assert.AreEqual("back", name);
        Assert.AreSame(cmdBack, cmd);
        CollectionAssert.AreEqual(new string[] { "somewhere", "safe" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_BackCommand_ValidSingleArg()
    {
        var result = parser.Parse("/back home", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("back", name);
        Assert.AreSame(cmdBack, cmd);
        CollectionAssert.AreEqual(new string[] { "home" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_NonExistentCommand()
    {
        var result = parser.Parse("/unknown", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.NoSuchCommand, result);
        Assert.AreEqual("unknown", name);
        Assert.IsNull(cmd);
        Assert.IsNull(args);
    }

    [TestMethod]
    public void Parse_EmptyCommandText_JustSlash()
    {
        var result = parser.Parse("/", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.NoSuchCommand, result);
        Assert.AreEqual("", name);
        Assert.IsNull(cmd);
        Assert.IsNull(args);
    }

    [TestMethod]
    public void Parse_CommandWithOnlyPrefixAndSpaces()
    {
        var result = parser.Parse("/   ", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.NoSuchCommand, result);
        Assert.AreEqual("", name);
        Assert.IsNull(cmd);
        Assert.IsNull(args);
    }

    [TestMethod]
    public void Parse_SayCommand_OnlyOneWord()
    {
        var result = parser.Parse("/say hello", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("say", name);
        Assert.AreSame(cmdSay, cmd);
        CollectionAssert.AreEqual(new string[] { "hello" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_SayCommand_EmptyMessage_ShouldBeMissingArguments()
    {
        var result = parser.Parse("/say ", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.MissingArguments, result);
        Assert.AreEqual("say", name);
        Assert.AreSame(cmdSay, cmd);
        CollectionAssert.AreEqual(new string[] { }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_SayCommand_OnlySpacesAfterCommand()
    {
        var result = parser.Parse("/say    ", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.MissingArguments, result);
        Assert.AreEqual("say", name);
        Assert.AreSame(cmdSay, cmd);
        CollectionAssert.AreEqual(new string[] { }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_WhisperCommand_MissingSecondArg()
    {
        var result = parser.Parse("/w Alice", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.MissingArguments, result);
        Assert.AreEqual("w", name);
        Assert.AreSame(cmdWhisper, cmd);
        CollectionAssert.AreEqual(new string[] { "Alice" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_WhisperCommand_ValidTwoArgsWithSpacesInSecond()
    {
        var result = parser.Parse("/w Charlie meet me at the    park", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("w", name);
        Assert.AreSame(cmdWhisper, cmd);
        CollectionAssert.AreEqual(new string[] { "Charlie", "meet me at the    park" }, (ICollection?)args);
    }

    [TestMethod]
    public void Parse_CommandNameCaseInsensitive()
    {
        var result = parser.Parse("/Say hello", out var name, out var cmd, out var args);
        Assert.AreEqual(CommandParser.ParseResult.Success, result);
        Assert.AreEqual("Say", name);
        Assert.AreSame(cmdSay, cmd);
        CollectionAssert.AreEqual(new string[] { "hello" }, (ICollection?)args);
    }
}
