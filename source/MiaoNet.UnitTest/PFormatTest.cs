using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class PFormatTest
{
    [TestMethod]
    public void TestFormat()
    {
        Assert.AreEqual("Hello", PFormat.Format("Hello", "World"));
        Assert.AreEqual("Hello World", PFormat.Format("Hello (0)", "World"));
        Assert.AreEqual("Hello World!", PFormat.Format("Hello (0)!", "World"));
        Assert.AreEqual("A B C", PFormat.Format("(0) (1) (2)", "A", "B", "C"));
        Assert.AreEqual("C B A", PFormat.Format("(2) (1) (0)", "A", "B", "C"));
        Assert.AreEqual("Hello 123", PFormat.Format("Hello (0)", 123));
        Assert.AreEqual("()", PFormat.Format("()"));
        Assert.AreEqual("(", PFormat.Format("("));
        Assert.AreEqual(")", PFormat.Format(")"));
        Assert.AreEqual("Hello (", PFormat.Format("Hello (", "World"));
        Assert.AreEqual("Hello ()", PFormat.Format("Hello ()", "World"));
        Assert.AreEqual("Hello World Extra", PFormat.Format("Hello (0) (1)", "World", "Extra"));
        Assert.AreEqual("Hello World (Extra)", PFormat.Format("Hello (0) ((1))", "World", "Extra"));
    }

    [TestMethod]
    public void TestIndexOutOfRange()
    {
        Assert.AreEqual("(1)", PFormat.Format("(1)", "arg"));
        Assert.AreEqual("Hello World (1)", PFormat.Format("Hello (0) (1)", "World"));
    }

    [TestMethod]
    public void TestNegativeIndex()
    {
        Assert.AreEqual("(-1)", PFormat.Format("(-1)", "arg"));
    }

    [TestMethod]
    public void TestInvalidIndex()
    {
        Assert.AreEqual("(a)", PFormat.Format("(a)"));
        Assert.AreEqual("Hello (a)", PFormat.Format("Hello (a)", "how"));
        Assert.AreEqual("Hello ((a)) (how)", PFormat.Format("Hello ((a)) ((0))", "how"));
    }
}
