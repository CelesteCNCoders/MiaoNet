using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class NameValidatorTests
{
    [TestMethod]
    public void AcceptsOrdinaryNames()
        => Assert.IsTrue(NameValidator.IsValid("Alice-01_x"));

    [TestMethod]
    public void RejectsEmptyOrNull()
    {
        Assert.IsFalse(NameValidator.IsValid(""));
        Assert.IsFalse(NameValidator.IsValid(null));
    }

    [TestMethod]
    public void RejectsWhitespace()
    {
        Assert.IsFalse(NameValidator.IsValid("Alice Bob"));
        Assert.IsFalse(NameValidator.IsValid(" Alice"));
        Assert.IsFalse(NameValidator.IsValid("Alice\tBob"));
    }

    [TestMethod]
    public void RejectsControlCharacters()
    {
        Assert.IsFalse(NameValidator.IsValid("Alice\u0000Bob"));
        Assert.IsFalse(NameValidator.IsValid("Alice\nBob"));
    }

    [TestMethod]
    public void RejectsUnicodeSpaces()
    {
        Assert.IsFalse(NameValidator.IsValid("Alice\u00A0Bob"));
        Assert.IsFalse(NameValidator.IsValid("Alice\u3000Bob"));
    }
}
