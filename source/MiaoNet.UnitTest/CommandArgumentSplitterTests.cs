using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class CommandArgumentSplitterTests
{
    private static string[] Split(string text, int contentStart, int segmentCount, bool captureRest)
        => CommandArgumentSplitter.Split(text, contentStart, segmentCount, captureRest)
            .Select(a => text[a.Start..a.End])
            .ToArray();

    [TestMethod]
    public void SplitsOnSpaces()
        => CollectionAssert.AreEqual(new[] { "Alice", "hi" }, Split("/w Alice hi", 2, 2, true));

    [TestMethod]
    public void RestArgumentKeepsInteriorSpaces()
        => CollectionAssert.AreEqual(new[] { "Alice", "hi there" }, Split("/w Alice hi there", 2, 2, true));

    [TestMethod]
    public void MissingRestArgumentIsDropped()
        => CollectionAssert.AreEqual(new[] { "Alice" }, Split("/w Alice ", 2, 2, true));

    [TestMethod]
    public void NonCaptureRestSplitsEveryArgument()
        => CollectionAssert.AreEqual(new[] { "foo", "bar" }, Split("/chat foo bar", 5, 1, false));

    [TestMethod]
    public void NonBreakingSpaceSeparatesArguments()
        => CollectionAssert.AreEqual(new[] { "Alice", "hi" }, Split("/w Alice\u00A0hi", 2, 2, true));

    [TestMethod]
    public void WhitespaceOnlyRestArgumentWithNonBreakingSpace_Terminates()
        => CollectionAssert.AreEqual(new[] { "Alice" }, Split("/w Alice \u00A0", 2, 2, true));

    [TestMethod]
    public void WhitespaceOnlyRestArgumentWithIdeographicSpace_Terminates()
        => CollectionAssert.AreEqual(new[] { "Alice" }, Split("/w Alice \u3000", 2, 2, true));
}
