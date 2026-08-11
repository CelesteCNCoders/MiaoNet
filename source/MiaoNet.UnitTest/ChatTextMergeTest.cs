using Celeste.Mod.ChatInputBox;

namespace ChatInputBox.UnitTest;

[TestClass]
public sealed class ChatTextMergeTest
{
    private readonly Color DefaultColor = ChatText.CommonColors[7]; // gray

    private static ChatText CreateText(string input, Color defaultColor)
        => ChatText.Create(input, defaultColor);

    [TestMethod]
    public void ContentEquals_SameInputParsedTwice_ReturnsTrue()
    {
        var a = CreateText(@"\cRed text", DefaultColor);
        var b = CreateText(@"\cRed text", DefaultColor);
        Assert.IsTrue(ChatTextMerge.ContentEquals(a, b));
    }

    [TestMethod]
    public void ContentEquals_DifferentText_ReturnsFalse()
    {
        var a = CreateText("hello", DefaultColor);
        var b = CreateText("world", DefaultColor);
        Assert.IsFalse(ChatTextMerge.ContentEquals(a, b));
    }

    [TestMethod]
    public void ContentEquals_DifferentColor_ReturnsFalse()
    {
        var a = CreateText(@"\cred", DefaultColor);
        var b = CreateText(@"\9red", DefaultColor);
        Assert.IsFalse(ChatTextMerge.ContentEquals(a, b));
    }

    [TestMethod]
    public void ContentEquals_DifferentStyle_ReturnsFalse()
    {
        var a = CreateText(@"\uunderlined", DefaultColor);
        var b = CreateText("underlined", DefaultColor);
        Assert.IsFalse(ChatTextMerge.ContentEquals(a, b));
    }

    [TestMethod]
    public void ContentEquals_DifferentSegmentCount_ReturnsFalse()
    {
        var a = CreateText("plain", DefaultColor);
        var b = CreateText(@"\1colored\rthen default", DefaultColor);
        Assert.IsFalse(ChatTextMerge.ContentEquals(a, b));
    }

    [TestMethod]
    public void ContentEquals_EmptyTexts_ReturnsTrue()
    {
        var a = CreateText("", DefaultColor);
        var b = CreateText("", DefaultColor);
        Assert.IsTrue(ChatTextMerge.ContentEquals(a, b));
    }

    [TestMethod]
    public void ShouldMerge_WithinWindow_ReturnsTrue()
    {
        Assert.IsTrue(ChatTextMerge.ShouldMerge(0f));
        Assert.IsTrue(ChatTextMerge.ShouldMerge(ChatTextMerge.MergeWindowSeconds));
        Assert.IsTrue(ChatTextMerge.ShouldMerge(ChatTextMerge.MergeWindowSeconds - 0.001f));
    }

    [TestMethod]
    public void ShouldMerge_OutsideWindow_ReturnsFalse()
    {
        Assert.IsFalse(ChatTextMerge.ShouldMerge(ChatTextMerge.MergeWindowSeconds + 0.001f));
        Assert.IsFalse(ChatTextMerge.ShouldMerge(float.MaxValue));
    }

    [TestMethod]
    public void GetCounterScale_SingleMessage_IsOne()
    {
        Assert.AreEqual(1f, ChatTextMerge.GetCounterScale(1));
    }

    [TestMethod]
    public void GetCounterScale_GrowsWithCount()
    {
        Assert.AreEqual(1.12f, ChatTextMerge.GetCounterScale(2), 1e-6f);
        Assert.AreEqual(1.24f, ChatTextMerge.GetCounterScale(3), 1e-6f);
        Assert.IsGreaterThan(ChatTextMerge.GetCounterScale(4), ChatTextMerge.GetCounterScale(5));
    }

    [TestMethod]
    public void GetCounterScale_CappedAtMax()
    {
        Assert.AreEqual(ChatTextMerge.MaxCounterScale, ChatTextMerge.GetCounterScale(100));
        Assert.AreEqual(ChatTextMerge.MaxCounterScale, ChatTextMerge.GetCounterScale(int.MaxValue));
    }

    [TestMethod]
    public void GetCounterShakeAmplitude_NoShakeAtTwoOrLess()
    {
        Assert.AreEqual(0f, ChatTextMerge.GetCounterShakeAmplitude(1));
        Assert.AreEqual(0f, ChatTextMerge.GetCounterShakeAmplitude(2));
    }

    [TestMethod]
    public void GetCounterShakeAmplitude_GrowsFromThree()
    {
        Assert.AreEqual(0.5f, ChatTextMerge.GetCounterShakeAmplitude(3), 1e-6f);
        Assert.AreEqual(1f, ChatTextMerge.GetCounterShakeAmplitude(4), 1e-6f);
    }

    [TestMethod]
    public void GetCounterShakeAmplitude_CappedAtMax()
    {
        Assert.AreEqual(ChatTextMerge.MaxCounterShakeAmplitude, ChatTextMerge.GetCounterShakeAmplitude(100));
        Assert.AreEqual(ChatTextMerge.MaxCounterShakeAmplitude, ChatTextMerge.GetCounterShakeAmplitude(int.MaxValue));
    }

    [TestMethod]
    public void GetCounterColorLerp_WhiteAtOne_RedAtNine()
    {
        Assert.AreEqual(0f, ChatTextMerge.GetCounterColorLerp(1));
        Assert.AreEqual(0.125f, ChatTextMerge.GetCounterColorLerp(2), 1e-6f);
        Assert.AreEqual(1f, ChatTextMerge.GetCounterColorLerp(9));
        Assert.AreEqual(1f, ChatTextMerge.GetCounterColorLerp(100));
    }

    [TestMethod]
    public void GetCounterPopScale_FadesToZero()
    {
        Assert.AreEqual(0.4f, ChatTextMerge.GetCounterPopScale(0f), 1e-6f);
        Assert.AreEqual(0f, ChatTextMerge.GetCounterPopScale(1f));
    }
}
