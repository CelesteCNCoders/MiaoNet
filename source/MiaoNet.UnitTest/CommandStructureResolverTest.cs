using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class CommandStructureResolverTest
{
    private static readonly IReadOnlyCollection<MiaoNetCommand> Commands =
        [new("say", null, [CommandSegmentType.Text], true, null!)];

    [TestMethod]
    public void EmptyText_ResolvesNoCommand()
    {
        var command = CommandStructureResolver.Resolve("", Commands, out int nameEnd, out var args);
        Assert.IsNull(command);
        Assert.AreEqual(0, nameEnd);
        Assert.IsEmpty(args);
    }
}
