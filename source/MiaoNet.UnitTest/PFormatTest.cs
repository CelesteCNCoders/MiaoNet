using System.Globalization;
using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class PFormatTest
{
    private static string PFormatI(string format, params object?[] args)
        => PFormat.Format(CultureInfo.InvariantCulture, format, args);

    [TestMethod]
    public void TestFormat()
    {
        Assert.AreEqual("Hello", PFormatI("Hello", "World"));
        Assert.AreEqual("Hello World", PFormatI("Hello (0)", "World"));
        Assert.AreEqual("Hello World!", PFormatI("Hello (0)!", "World"));
        Assert.AreEqual("A B C", PFormatI("(0) (1) (2)", "A", "B", "C"));
        Assert.AreEqual("C B A", PFormatI("(2) (1) (0)", "A", "B", "C"));
        Assert.AreEqual("Hello 123", PFormatI("Hello (0)", 123));
        Assert.AreEqual("()", PFormatI("()"));
        Assert.AreEqual("(", PFormatI("("));
        Assert.AreEqual(")", PFormatI(")"));
        Assert.AreEqual("Hello (", PFormatI("Hello (", "World"));
        Assert.AreEqual("Hello ()", PFormatI("Hello ()", "World"));
        Assert.AreEqual("Hello World Extra", PFormatI("Hello (0) (1)", "World", "Extra"));
        Assert.AreEqual("Hello World (Extra)", PFormatI("Hello (0) ((1))", "World", "Extra"));
    }

    [TestMethod]
    public void TestIndexOutOfRange()
    {
        Assert.AreEqual("(1)", PFormatI("(1)", "arg"));
        Assert.AreEqual("Hello World (1)", PFormatI("Hello (0) (1)", "World"));
    }

    [TestMethod]
    public void TestNegativeIndex()
    {
        Assert.AreEqual("(-1)", PFormatI("(-1)", "arg"));
    }

    [TestMethod]
    public void TestInvalidIndex()
    {
        Assert.AreEqual("(a)", PFormatI("(a)"));
        Assert.AreEqual("Hello (a)", PFormatI("Hello (a)", "how"));
        Assert.AreEqual("Hello ((a)) (how)", PFormatI("Hello ((a)) ((0))", "how"));
    }

    [TestMethod]
    public void TestCultureInfo()
    {
        var cRu = new CultureInfo("ru-RU");
        Assert.AreEqual("1,2 2,3 3,4", PFormat.Format(cRu, "(0) (1) (2)", 1.2f, 2.3f, 3.4f));

        var cEn = new CultureInfo("en-US");
        Assert.AreEqual("1.2 2.3 3.4", PFormat.Format(cEn, "(0) (1) (2)", 1.2f, 2.3f, 3.4f));

        var cFr = new CultureInfo("fr-FR");
        Assert.AreEqual("1,2 2,3 3,4", PFormat.Format(cFr, "(0) (1) (2)", 1.2f, 2.3f, 3.4f));

        var cDe = new CultureInfo("de-DE");
        Assert.AreEqual("1,2 2,3 3,4", PFormat.Format(cDe, "(0) (1) (2)", 1.2f, 2.3f, 3.4f));

        Assert.AreEqual("1000,5 2000,5", PFormat.Format(cRu, "(0) (1)", 1000.5, 2000.5m));

        Assert.AreEqual("-10,5 10,5", PFormat.Format(cDe, "(0) (1)", -10.5, 10.5));
    }

    [TestMethod]
    public void TestNullArguments()
    {
        Assert.AreEqual("Hello ", PFormatI("Hello (0)", [null]));
        Assert.AreEqual("()", PFormatI("()", [null]));
    }

    [TestMethod]
    public void TestMultipleReplaceSameIndex()
    {
        Assert.AreEqual("A A A", PFormatI("(0) (0) (0)", "A"));
        Assert.AreEqual("123 123", PFormatI("(0) (0)", 123));
    }

    [TestMethod]
    public void TestNestedParentheses()
    {
        Assert.AreEqual("(A)", PFormatI("((0))", "A"));
        Assert.AreEqual("((A))", PFormatI("(((0)))", "A"));
    }
}
