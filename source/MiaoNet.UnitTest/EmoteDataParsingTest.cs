#pragma warning disable CA1861

using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class EmoteDataParsingTest
{
    [TestMethod]
    public void TryParse_ValidFullFormat_ReturnsCorrectEmoteData()
    {
        var success = EmoteData.TryParse("p24:theo/yolo 03 02 01 02 !", out var result);
        Assert.IsTrue(success);
        Assert.AreEqual(EmoteAtlasCategory.Portrait, result.Category);
        Assert.AreEqual(24, result.Fps);
        Assert.AreEqual("theo/yolo", result.Prefix);
        CollectionAssert.AreEqual(new[] { "03", "02", "01", "02" }, result.Frames.ToList());
        Assert.IsFalse(result.Loop);
    }

    [TestMethod]
    public void TryParse_DefaultFpsAndLoopTrue()
    {
        var success = EmoteData.TryParse("p:theo/yolo0 3 2 1 2", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual(EmoteData.DefaultFps, emote.Fps);
        Assert.IsTrue(emote.Loop);
        Assert.AreEqual("theo/yolo0", emote.Prefix);
        CollectionAssert.AreEqual(new[] { "3", "2", "1", "2" }, emote.Frames.ToList());
    }

    [TestMethod]
    public void TryParse_SingleFrameWithEmptyString()
    {
        var success = EmoteData.TryParse("i:strawberry", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual(EmoteAtlasCategory.Gui, emote.Category);
        Assert.AreEqual("strawberry", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsTrue(emote.Loop);
    }

    [TestMethod]
    public void TryParse_SingleFrameWithExplicitFpsAndNoLoop()
    {
        var success = EmoteData.TryParse("i10:spike !", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual(10, emote.Fps);
        Assert.AreEqual("spike", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsFalse(emote.Loop);
    }

    [TestMethod]
    public void TryParse_EmptyOrWhitespaceInput_ReturnsFalse()
    {
        Assert.IsFalse(EmoteData.TryParse("", out _));
        Assert.IsFalse(EmoteData.TryParse("   ", out _));
        Assert.IsFalse(EmoteData.TryParse("\t\n", out _));
    }

    [TestMethod]
    public void TryParse_InvalidCategory_ReturnsFalse()
    {
        Assert.IsFalse(EmoteData.TryParse("x:strawberry", out _));
        Assert.IsFalse(EmoteData.TryParse("2:strawberry !", out _));
    }

    [TestMethod]
    public void TryParse_MissingColon_ReturnsFalse()
    {
        Assert.IsFalse(EmoteData.TryParse("pno_colon", out _));
        Assert.IsFalse(EmoteData.TryParse("i", out _));
    }

    [TestMethod]
    public void TryParse_InvalidFps_ReturnsFalse()
    {
        Assert.IsFalse(EmoteData.TryParse("pabc:prefix", out _));
        Assert.IsFalse(EmoteData.TryParse("p-5:prefix", out _));
        Assert.IsFalse(EmoteData.TryParse("p99999:prefix", out _)); // out of ushort range
    }

    [TestMethod]
    public void TryParse_NoPrefixProvided()
    {
        var success = EmoteData.TryParse("p: test a b c", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual(string.Empty, emote.Prefix);
        CollectionAssert.AreEqual(new[] { "test", "a", "b", "c" }, emote.Frames.ToList());
    }

    [TestMethod]
    public void TryParse_EmptyPrefix_IsCurrentlyAllowed()
    {
        var success = EmoteData.TryParse("p:", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual("", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsTrue(emote.Loop);
    }

    [TestMethod]
    public void TryParse_GameplayCategoryWithBang()
    {
        var success = EmoteData.TryParse("g5:explosion boom crash !", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual(EmoteAtlasCategory.Gameplay, emote.Category);
        Assert.AreEqual(5, emote.Fps);
        Assert.AreEqual("explosion", emote.Prefix);
        CollectionAssert.AreEqual(new[] { "boom", "crash" }, emote.Frames.ToList());
        Assert.IsFalse(emote.Loop);
    }

    [TestMethod]
    public void TryParse_NoFramesButHasBang_ResultsInEmptyStringFrame()
    {
        var success = EmoteData.TryParse("i:icon !", out var emote);
        Assert.IsTrue(success);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsFalse(emote.Loop);
    }

    [TestMethod]
    public void TryParse_OnlyCategoryAndFpsColonButNoPrefix_ReturnsTrue()
    {
        var success = EmoteData.TryParse("p10:", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual("", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsTrue(emote.Loop);
    }

    [TestMethod]
    public void TryParse_TrailingSpacesAreIgnored()
    {
        var success = EmoteData.TryParse(" i:strawberry  ", out var emote);
        Assert.IsTrue(success);
        Assert.AreEqual("strawberry", emote.Prefix);
    }
}
