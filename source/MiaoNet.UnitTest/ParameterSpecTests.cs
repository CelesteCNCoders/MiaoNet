using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Chat;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.PlayerList;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace MiaoNet.UnitTest;

// executable assertions from the parameter spec, labelled by parameter id so the doc and the tests
// can be cross-checked. A series and B1-B7 live in UiLayoutTests/ChatUiTests; C, E, F and G9-G11
// live in ChatUiTests/TextEditingUiTests/PlayerListUiTests.
[TestClass]
public sealed class ParameterSpecTests
{
    private const float LineHeight = 24f;
    private const float CharWidth = 10f;

    private static void AssertClose(float expected, float actual, string message)
        => Assert.IsLessThan(1e-4f, MathF.Abs(expected - actual), $"{message}: expected {expected}, got {actual}");

    private sealed class FakeTextRenderer : ITextRenderer
    {
        public UiSize Measure(string text, TextStyle style)
            => new(text.Length * CharWidth * style.Scale, 12f * style.Scale);

        public void Draw(IUiCanvas canvas, string text, UiOffset position, TextStyle style)
        {
        }

        public bool CanRender(int character, TextStyle style) => true;
    }

    private sealed class FakeTexture(float width, float height) : IUiTexture
    {
        public float Width { get; } = width;

        public float Height { get; } = height;

        public void Draw(
            IUiCanvas canvas,
            UiOffset position,
            UiColor tint,
            float scale,
            HorizontalAnchor horizontalAnchor = HorizontalAnchor.Left,
            VerticalAnchor verticalAnchor = VerticalAnchor.Top)
        {
        }
    }

    private sealed class RecordingCanvas : IUiCanvas
    {
        public List<UiRect> Fills { get; } = [];

        public List<UiColor> FillColors { get; } = [];

        public void FillRect(UiRect rect, UiColor color)
        {
            Fills.Add(rect);
            FillColors.Add(color);
        }

        public void DrawLine(UiOffset from, UiOffset to, UiColor color, float thickness)
        {
        }

        public void PushClip(UiRect rect)
        {
        }

        public void PopClip()
        {
        }
    }

    // ---------------------------------------------------------------- name clipping

    [TestMethod]
    public void G8_MapAndRoomNamesAreClippedAtTwentyFourCharacters()
    {
        string atLimit = new('a', PlayerListLayout.ClipLength);
        string overLimit = new('a', PlayerListLayout.ClipLength + 1);

        Assert.AreEqual(atLimit, PlayerListLayout.Clip(atLimit, ClipType.KeepPrefix), "G8 at the limit is unchanged");
        Assert.AreEqual(atLimit + "...", PlayerListLayout.Clip(overLimit, ClipType.KeepPrefix), "G8 keep prefix");
        Assert.AreEqual("..." + atLimit, PlayerListLayout.Clip(overLimit, ClipType.KeepSuffix), "G8 keep suffix");
        Assert.AreEqual(overLimit, PlayerListLayout.Clip(overLimit, ClipType.None), "G8 None leaves it alone");
    }

    [TestMethod]
    public void UiColor_ScalingMultipliesAllFourChannelsLikeXna()
    {
        // XNA's Color * float scales RGB too: every dimmed or faded color scales all four channels,
        // so getting it wrong leaves faded text full-bright.
        UiColor half = UiColor.White * 0.5f;
        AssertClose(0.5f, half.R, "scaled R");
        AssertClose(0.5f, half.G, "scaled G");
        AssertClose(0.5f, half.B, "scaled B");
        AssertClose(0.5f, half.A, "scaled A");

        // black is unaffected by the scale, so the background constants hold either way
        Assert.AreEqual(UiColor.Black * 0.5f, new UiColor(0f, 0f, 0f, 0.5f), "black stays black");
    }

    [TestMethod]
    public void Tab_IdleLabelIsDimmedInBrightnessAsWellAsAlpha()
    {
        // the idle label is Color.White * 0.5f, i.e. grey at half alpha
        AssertClose(0.5f, MiaoNetUiTheme.Tab.IdleText.R, "idle label R is dimmed");
        AssertClose(0.5f, MiaoNetUiTheme.Tab.IdleText.G, "idle label G is dimmed");
        AssertClose(0.5f, MiaoNetUiTheme.Tab.IdleText.B, "idle label B is dimmed");
        AssertClose(0.5f, MiaoNetUiTheme.Tab.IdleText.A, "idle label alpha");

        AssertClose(1f, MiaoNetUiTheme.Tab.ActiveText.R, "active label stays full brightness");
        AssertClose(1f, MiaoNetUiTheme.Tab.ActiveText.A, "active label stays opaque");
    }

    // ---------------------------------------------------------------- palette

    [TestMethod]
    public void Theme_MatchesTheDocumentedPalette()
    {
        // the alpha values are the fraction multiplied in, e.g. 0x7f / 255 for the input box
        // background.
        Assert.AreEqual(UiColor.Black, MiaoNetUiTheme.Chat.Background, "COLOR.CHAT.BG");
        Assert.AreEqual(UiColor.CornflowerBlue, MiaoNetUiTheme.Chat.Time, "COLOR.CHAT.TIME");
        Assert.AreEqual(UiColor.White, MiaoNetUiTheme.Chat.DefaultText, "COLOR.CHAT.TEXT");

        Assert.AreEqual(UiColor.Black * 0.5f, MiaoNetUiTheme.Tab.ActiveBackground, "COLOR.TAB.BG active");
        Assert.AreEqual(UiColor.Black * 0.15f, MiaoNetUiTheme.Tab.IdleBackground, "COLOR.TAB.BG idle");
        Assert.AreEqual(UiColor.White, MiaoNetUiTheme.Tab.ActiveText, "COLOR.TAB.TEXT active");
        Assert.AreEqual(UiColor.White * 0.5f, MiaoNetUiTheme.Tab.IdleText, "COLOR.TAB.TEXT idle");

        Assert.AreEqual(UiColor.Black * (0x7f / 255f), MiaoNetUiTheme.Input.Background, "COLOR.INPUT.BG");
        Assert.AreEqual(UiColor.White, MiaoNetUiTheme.Input.Text, "COLOR.INPUT.TEXT");
        Assert.AreEqual(UiColor.Gray, MiaoNetUiTheme.Input.ImeText, "COLOR.INPUT.IME");
        Assert.AreEqual(UiColor.White, MiaoNetUiTheme.Input.Caret, "COLOR.INPUT.CARET");

        Assert.AreEqual(UiColor.Black * (0xaa / 255f), MiaoNetUiTheme.Completion.Background, "COLOR.COMPL.BG");
        Assert.AreEqual(UiColor.Cyan, MiaoNetUiTheme.Completion.BorderTop, "COLOR.COMPL.BORDER_TOP");
        Assert.AreEqual(UiColor.CornflowerBlue, MiaoNetUiTheme.Completion.BorderLeft, "COLOR.COMPL.BORDER_LEFT");
        Assert.AreEqual(UiColor.LightGray, MiaoNetUiTheme.Completion.Text, "COLOR.COMPL.TEXT");
        Assert.AreEqual(UiColor.White, MiaoNetUiTheme.Completion.SelectedText, "COLOR.COMPL.TEXT selected");
        Assert.AreEqual(UiColor.Wheat * (0x22 / 255f), MiaoNetUiTheme.Completion.SelectedBackground, "COLOR.COMPL.SEL_BG");
        Assert.AreEqual(UiColor.Wheat, MiaoNetUiTheme.Completion.SelectedBar, "COLOR.COMPL.SEL_BAR");

        Assert.AreEqual(UiColor.Black * (0xcc / 255f), MiaoNetUiTheme.PlayerList.Background, "COLOR.PL.BG");
        Assert.AreEqual(UiColor.CornflowerBlue, MiaoNetUiTheme.PlayerList.BorderTop, "COLOR.PL.BORDER_TOP");
        Assert.AreEqual(UiColor.Cyan, MiaoNetUiTheme.PlayerList.BorderLeft, "COLOR.PL.BORDER_LEFT");
        Assert.AreEqual(UiColor.Yellow, MiaoNetUiTheme.PlayerList.Header, "COLOR.PL.HEADER");
        Assert.AreEqual(UiColor.FromBytes(0x00, 0x00, 0x00, 0x22), MiaoNetUiTheme.PlayerList.StripeEven, "COLOR.PL.STRIPE_EVEN");
        Assert.AreEqual(UiColor.FromBytes(0x22, 0x22, 0x22, 0x88), MiaoNetUiTheme.PlayerList.StripeOdd, "COLOR.PL.STRIPE_ODD");
        Assert.AreEqual(UiColor.LightGray, MiaoNetUiTheme.PlayerList.Ping, "COLOR.PL.PING");
        Assert.AreEqual(UiColor.LightGray, MiaoNetUiTheme.PlayerList.Room, "COLOR.PL.ROOM");
        Assert.AreEqual(UiColor.White, MiaoNetUiTheme.PlayerList.Icon, "COLOR.PL.ICON");
    }

    // ---------------------------------------------------------------- scissor mapping

    [TestMethod]
    public void Scissor_IdentityTransformFloorsOriginAndCeilsExtent()
    {
        UiPixelRect mapped = UiScissor.Map(
            new UiRect(10.4f, 20.6f, 30f, 40f),
            UiTransform2D.Identity,
            1920,
            1080);

        Assert.AreEqual(10, mapped.X, "floor left");
        Assert.AreEqual(20, mapped.Y, "floor top");
        Assert.AreEqual(31, mapped.Width, "ceil(40.4) - 10");
        Assert.AreEqual(41, mapped.Height, "ceil(60.6) - 20");
    }

    [TestMethod]
    public void Scissor_AppliesTheScreenMatrixScale()
    {
        // A 2x scale, as a windowed backbuffer would apply to logical coordinates.
        var transform = new UiTransform2D(2f, 0f, 0f, 2f, 0f, 0f);

        UiPixelRect mapped = UiScissor.Map(new UiRect(10f, 20f, 30f, 40f), transform, 1920, 1080);

        Assert.AreEqual(20, mapped.X);
        Assert.AreEqual(40, mapped.Y);
        Assert.AreEqual(60, mapped.Width);
        Assert.AreEqual(80, mapped.Height);
    }

    [TestMethod]
    public void Scissor_AppliesTheScreenMatrixTranslation()
    {
        var transform = new UiTransform2D(1f, 0f, 0f, 1f, 7f, 11f);

        UiPixelRect mapped = UiScissor.Map(new UiRect(0f, 0f, 100f, 50f), transform, 1920, 1080);

        Assert.AreEqual(7, mapped.X);
        Assert.AreEqual(11, mapped.Y);
        Assert.AreEqual(100, mapped.Width);
        Assert.AreEqual(50, mapped.Height);
    }

    [TestMethod]
    public void Scissor_ClampsToTheViewport()
    {
        UiPixelRect mapped = UiScissor.Map(
            new UiRect(-50f, -50f, 200f, 200f),
            UiTransform2D.Identity,
            100,
            80);

        Assert.AreEqual(0, mapped.X, "negative origin clamps to 0");
        Assert.AreEqual(0, mapped.Y);
        Assert.AreEqual(100, mapped.Width, "clamped to the viewport width");
        Assert.AreEqual(80, mapped.Height);

        UiPixelRect outside = UiScissor.Map(
            new UiRect(500f, 500f, 10f, 10f),
            UiTransform2D.Identity,
            100,
            80);
        Assert.AreEqual(0, outside.Width, "fully outside yields an empty rect");
        Assert.AreEqual(0, outside.Height);
    }

    // ---------------------------------------------------------------- fold counter

    [TestMethod]
    public void B8_FoldCounter_ScaleAndPopAtTheLowEnd()
    {
        AssertClose(1f, FoldCounter.GetScale(1), "B8 scale at count 1");
        AssertClose(1.12f, FoldCounter.GetScale(2), "B8 scale steps by 0.12");
        AssertClose(0f, FoldCounter.GetShakeAmplitude(2), "B8 no shake below count 3");
        AssertClose(0.4f, FoldCounter.GetPopScale(0f), "B8 pop starts at 0.4");
        AssertClose(0f, FoldCounter.GetPopScale(1f), "B8 pop is gone at progress 1");
    }

    [TestMethod]
    public void B9_FoldCounter_ScaleAndShakeSaturate()
    {
        AssertClose(2.2f, FoldCounter.GetScale(11), "B9 scale cap");
        AssertClose(4.5f, FoldCounter.GetShakeAmplitude(11), "B9 shake steps by 0.5");
        AssertClose(6f, FoldCounter.GetShakeAmplitude(100), "B9 shake cap");
    }

    [TestMethod]
    public void B10_FoldCounter_ColorTurnsRainbowPastTheRedCount()
    {
        RgbColor atRed = FoldCounter.GetColor(FoldCounter.MaxRedCount, 0f);
        AssertClose(1f, atRed.R, "B10 full red R");
        AssertClose(0f, atRed.G, "B10 full red G");

        // Past MaxRedCount the hue cycles with the animation clock (RainbowCyclePeriod = 3s).
        RgbColor start = FoldCounter.GetColor(FoldCounter.MaxRedCount + 1, 0f);
        RgbColor half = FoldCounter.GetColor(FoldCounter.MaxRedCount + 1, FoldCounter.RainbowCyclePeriod / 2f);
        Assert.AreNotEqual(start, half, "B10 hue advances with the clock");
    }

    // ---------------------------------------------------------------- tab bar

    private static (ChatTabBarNode Tabs, RecordingCanvas Canvas) PaintTabBar(int activeIndex, params string[] titles)
    {
        var tabs = new ChatTabBarNode(new FakeTextRenderer())
        {
            InitialTitle = "Global",
            Tabs = titles,
            ActiveIndex = activeIndex,
            LineHeight = LineHeight,
            Scale = 1f,
        };

        var host = new AlignNode
        {
            Alignment = UiAlignment.TopLeft,
            Child = tabs,
            Style = new UiStyle { Width = 400f, Height = LineHeight },
        };

        var ui = new UiRoot();
        ui.SetRoot(host);
        ui.Layout(400f, LineHeight);

        var canvas = new RecordingCanvas();
        tabs.PaintTree(canvas, 1f);
        return (tabs, canvas);
    }

    [TestMethod]
    public void D2_TabWidthIsTextPlusBothPaddings()
    {
        (ChatTabBarNode tabs, RecordingCanvas canvas) = PaintTabBar(-1);

        // "Global" is 60 wide, plus 2 * ChatTab.Padding = 16.
        AssertClose(60f + 16f, canvas.Fills[0].Width, "D2 first tab width");
        AssertClose(LineHeight, canvas.Fills[0].Height, "D2 tab height is the line height");
        AssertClose(1, tabs.EntryCount, "only the initial tab exists");
    }

    [TestMethod]
    public void D3_TabsAdvanceByWidthPlusGap()
    {
        (_, RecordingCanvas canvas) = PaintTabBar(-1, "A", "BB");

        Assert.HasCount(3, canvas.Fills, "initial tab plus two channels");

        // "Global" 60 -> 76; "A" 10 -> 26; "BB" 20 -> 36; gap 2 between tabs.
        AssertClose(0f, canvas.Fills[0].X, "D3 tab 0 X");
        AssertClose(78f, canvas.Fills[1].X, "D3 tab 1 X");
        AssertClose(106f, canvas.Fills[2].X, "D3 tab 2 X");
    }

    [TestMethod]
    public void D5_ActiveTabUsesTheActivePaletteEntry()
    {
        // CHAT.TAB.FIRST: the initial tab is addressed by index -1, channels by 0..n-1.
        (_, RecordingCanvas initialActive) = PaintTabBar(-1, "A");
        Assert.AreEqual(MiaoNetUiTheme.Tab.ActiveBackground, initialActive.FillColors[0], "D5 initial tab is active");
        Assert.AreEqual(MiaoNetUiTheme.Tab.IdleBackground, initialActive.FillColors[1], "D5 channel tab is idle");

        (_, RecordingCanvas channelActive) = PaintTabBar(0, "A");
        Assert.AreEqual(MiaoNetUiTheme.Tab.IdleBackground, channelActive.FillColors[0], "D5 initial tab is idle");
        Assert.AreEqual(MiaoNetUiTheme.Tab.ActiveBackground, channelActive.FillColors[1], "D5 channel tab is active");
    }

    // ---------------------------------------------------------------- player row width

    [TestMethod]
    public void G4_RowContentWidthSumsNameIconsAndLocationGroup()
    {
        var icons = new PlayerListIcons { Paused = new FakeTexture(10f, LineHeight) };
        var row = new PlayerRow
        {
            DisplayName = "ABCDE",            // 50
            Status = PlayerStatus.Paused,     // icon 10 + 2 * PausedGap 4 = 18
            HasLocation = true,
            RoomText = "ROOM",                // 40
            MapName = "MAP",                  // 30
        };

        PlayerListMetrics metrics = PlayerListMetrics.Compute(
            [new PlayerListChannel { Header = "H", Rows = [row] }],
            new FakeTextRenderer(),
            scale: 1f,
            lineHeight: LineHeight,
            icons);

        // name 50 + MiddlePadding 32 + paused 18 + colon 10 + room 40 + space 10 + map 30 = 190
        // RowWidth = 190 + ping 0 + 2 * RowPaddingX 8 = 198
        AssertClose(198f, metrics.RowWidth, "G4 row width");
        AssertClose(198f + 32f, metrics.PanelWidth, "G4 panel width");
        AssertClose(28f, metrics.RowHeight, "G1 row height = lh + 2 * 2");
        AssertClose(LineHeight, metrics.HeaderHeight, "G5 header height");
    }

    [TestMethod]
    public void G4_PingColumnIsReservedOnEveryRow()
    {
        var icons = new PlayerListIcons();
        var withPing = new PlayerRow { DisplayName = "A", PingText = "999ms" };
        var withoutPing = new PlayerRow { DisplayName = "A" };

        PlayerListMetrics metrics = PlayerListMetrics.Compute(
            [new PlayerListChannel { Header = "H", Rows = [withPing, withoutPing] }],
            new FakeTextRenderer(),
            scale: 1f,
            lineHeight: LineHeight,
            icons);

        // "999ms" is 50 wide plus spaceWidth 10 = 60 reserved for both rows.
        AssertClose(60f, metrics.PingColumnWidth, "G4 ping column width");
        // maxLine = max(header 10, name 10 + 32) = 42; rowWidth = 42 + 60 + 8 = 110
        AssertClose(110f, metrics.RowWidth, "G4 row width with ping column");
    }
}
