using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class CommandStructureResolverTest
{
    private static readonly IReadOnlyCollection<MiaoNetCommand> Commands =
    [
        new("say", null, [CommandSegmentType.Text], true, null!),
        new("whisper", ["w"], [CommandSegmentType.Player, CommandSegmentType.Text], true, null!),
        new("test", null, [CommandSegmentType.Text, CommandSegmentType.Text], false, null!)
    ];

    [TestMethod]
    public void EmptyText_ResolvesNoCommand()
    {
        var command = CommandStructureResolver.Resolve("", Commands, out int nameEnd, out var args);
        Assert.IsNull(command);
        Assert.AreEqual(0, nameEnd);
        Assert.IsEmpty(args);
    }

    [TestMethod]
    public void CommandName_EndsAtFirstWhitespace()
    {
        var command = CommandStructureResolver.Resolve("/say hello world", Commands, out int nameEnd, out var args);

        Assert.AreSame(Commands.First(), command);
        Assert.AreEqual(4, nameEnd);
        Assert.HasCount(1, args);
        Assert.AreEqual(5, args[0].Start);
        Assert.AreEqual(16, args[0].End);
    }

    [TestMethod]
    public void CommandName_EndsAtEndOfText_WhenNoWhitespace()
    {
        var command = CommandStructureResolver.Resolve("/say", Commands, out int nameEnd, out var args);

        Assert.AreSame(Commands.First(), command);
        Assert.AreEqual(4, nameEnd);
        Assert.IsEmpty(args);
    }

    [TestMethod]
    public void NonAsciiWhitespace_EndsCommandName()
    {
        var command = CommandStructureResolver.Resolve("/w\u00A0Alice hi", Commands, out int nameEnd, out var args);

        Assert.AreEqual(2, nameEnd);
        Assert.AreSame(Commands.ElementAt(1), command);
        Assert.HasCount(2, args);
        Assert.AreEqual(3, args[0].Start);
        Assert.AreEqual(8, args[0].End);
    }

    [TestMethod]
    public void UnknownCommand_HasNoArguments()
    {
        var command = CommandStructureResolver.Resolve("/nope x", Commands, out int nameEnd, out var args);

        Assert.IsNull(command);
        Assert.AreEqual(5, nameEnd);
        Assert.IsEmpty(args);
    }

    [TestMethod]
    public void Alias_MatchesCommand_AndMapsSegmentTypes()
    {
        var command = CommandStructureResolver.Resolve("/w Alice hi", Commands, out int nameEnd, out var args);

        Assert.AreSame(Commands.ElementAt(1), command);
        Assert.AreEqual(2, nameEnd);
        Assert.HasCount(2, args);

        Assert.AreEqual(CommandSegmentType.Player, args[0].SegmentType);
        Assert.IsFalse(args[0].FreeText);
        Assert.AreEqual(3, args[0].Start);
        Assert.AreEqual(8, args[0].End);

        Assert.AreEqual(CommandSegmentType.Text, args[1].SegmentType);
        Assert.IsTrue(args[1].FreeText);
        Assert.AreEqual(9, args[1].Start);
        Assert.AreEqual(11, args[1].End);
    }

    [TestMethod]
    public void CaptureRest_KeepsInteriorWhitespace()
    {
        var command = CommandStructureResolver.Resolve("/say hello   world", Commands, out _, out var args);

        Assert.IsNotNull(command);
        Assert.HasCount(1, args);
        Assert.AreEqual(5, args[0].Start);
        Assert.AreEqual(18, args[0].End);
    }

    [TestMethod]
    public void ExtraArguments_AreMarkedOverflow()
    {
        var command = CommandStructureResolver.Resolve("/test a b c", Commands, out _, out var args);

        Assert.IsNotNull(command);
        Assert.HasCount(3, args);

        Assert.IsFalse(args[0].IsOverflow);
        Assert.IsTrue(args[0].FreeText);
        Assert.IsFalse(args[1].IsOverflow);
        Assert.IsTrue(args[1].FreeText);

        Assert.IsTrue(args[2].IsOverflow);
        Assert.AreEqual(CommandSegmentType.Text, args[2].SegmentType);
        Assert.IsFalse(args[2].FreeText);
    }
}
