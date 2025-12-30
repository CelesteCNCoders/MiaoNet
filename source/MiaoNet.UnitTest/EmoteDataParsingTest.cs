#pragma warning disable CA1861

using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class EmoteDataParsingTest
{
    [TestMethod]
    public void Parse_ValidFullFormat_ReturnsCorrectEmoteData()
    {
        var result = EmoteData.Parse("p24:theo/yolo 03 02 01 02 !");
        Assert.IsNotNull(result);
        Assert.AreEqual(EmoteAtlasCategory.Portrait, result.Value.Category);
        Assert.AreEqual(24, result.Value.Fps);
        Assert.AreEqual("theo/yolo", result.Value.Prefix);
        CollectionAssert.AreEqual(new[] { "03", "02", "01", "02" }, result.Value.Frames.ToList());
        Assert.IsFalse(result.Value.Loop);
    }

    [TestMethod]
    public void Parse_DefaultFpsAndLoopTrue()
    {
        var result = EmoteData.Parse("p:theo/yolo0 3 2 1 2");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual(EmoteData.DefaultFps, emote.Fps);
        Assert.IsTrue(emote.Loop);
        Assert.AreEqual("theo/yolo0", emote.Prefix);
        CollectionAssert.AreEqual(new[] { "3", "2", "1", "2" }, emote.Frames.ToList());
    }

    [TestMethod]
    public void Parse_SingleFrameWithEmptyString()
    {
        var result = EmoteData.Parse("i:strawberry");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual(EmoteAtlasCategory.Gui, emote.Category);
        Assert.AreEqual("strawberry", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsTrue(emote.Loop);
    }

    [TestMethod]
    public void Parse_SingleFrameWithExplicitFpsAndNoLoop()
    {
        var result = EmoteData.Parse("i10:spike !");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual(10, emote.Fps);
        Assert.AreEqual("spike", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsFalse(emote.Loop);
    }

    [TestMethod]
    public void Parse_EmptyOrWhitespaceInput_ReturnsNull()
    {
        Assert.IsNull(EmoteData.Parse(""));
        Assert.IsNull(EmoteData.Parse("   "));
        Assert.IsNull(EmoteData.Parse("\t\n"));
    }

    [TestMethod]
    public void Parse_InvalidCategory_ReturnsNull()
    {
        Assert.IsNull(EmoteData.Parse("x:strawberry"));
        Assert.IsNull(EmoteData.Parse("2:strawberry !"));
    }

    [TestMethod]
    public void Parse_MissingColon_ReturnsNull()
    {
        Assert.IsNull(EmoteData.Parse("pno_colon"));
        Assert.IsNull(EmoteData.Parse("i"));
    }

    [TestMethod]
    public void Parse_InvalidFps_ReturnsNull()
    {
        Assert.IsNull(EmoteData.Parse("pabc:prefix"));
        Assert.IsNull(EmoteData.Parse("p-5:prefix"));
        Assert.IsNull(EmoteData.Parse("p99999:prefix")); // out of ushort range
    }

    [TestMethod]
    public void Parse_PrefixCannotContainSpace_ButParserTreatsFirstTokenAsPrefix()
    {
        var result = EmoteData.Parse("p:test a b c");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual("test", emote.Prefix);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, emote.Frames.ToList());
    }

    [TestMethod]
    public void Parse_EmptyPrefix_IsCurrentlyAllowed()
    {
        var result = EmoteData.Parse("p:");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual("", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsTrue(emote.Loop);
    }

    [TestMethod]
    public void Parse_GameplayCategoryWithBang()
    {
        var result = EmoteData.Parse("g5:explosion boom crash !");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual(EmoteAtlasCategory.Gameplay, emote.Category);
        Assert.AreEqual(5, emote.Fps);
        Assert.AreEqual("explosion", emote.Prefix);
        CollectionAssert.AreEqual(new[] { "boom", "crash" }, emote.Frames.ToList());
        Assert.IsFalse(emote.Loop);
    }

    [TestMethod]
    public void Parse_NoFramesButHasBang_ResultsInEmptyStringFrame()
    {
        var result = EmoteData.Parse("i:icon !");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsFalse(emote.Loop);
    }

    [TestMethod]
    public void Parse_OnlyCategoryAndFpsColonButNoPrefix_ReturnsNonNull()
    {
        var result = EmoteData.Parse("p10:");
        Assert.IsTrue(result.HasValue);
        var emote = result.Value;
        Assert.AreEqual("", emote.Prefix);
        Assert.HasCount(1, emote.Frames);
        Assert.AreEqual(string.Empty, emote.Frames[0]);
        Assert.IsTrue(emote.Loop);
    }

    [TestMethod]
    public void Parse_TrailingSpacesAreIgnored()
    {
        var result = EmoteData.Parse(" i:strawberry  ");
        Assert.IsTrue(result.HasValue);
        Assert.AreEqual("strawberry", result.Value.Prefix);
    }
}
