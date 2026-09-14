using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.PlayerList;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace MiaoNet.UnitTest;

// player list geometry: width derivation, node layout, the scroll upper bound and viewport culling
[TestClass]
public sealed class PlayerListUiTests
{
    private const float LineHeight = 24f;
    private const float CharWidth = 10f;

    private static void AssertClose(float expected, float actual, string message)
        => Assert.IsLessThan(1e-4f, MathF.Abs(expected - actual), $"{message}: expected {expected}, got {actual}");

    // deterministic stand-in for the pixel font: every glyph is CharWidth wide
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
        public List<UiColor> FillColors { get; } = [];

        public void FillRect(UiRect rect, UiColor color) => FillColors.Add(color);

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

    private static PlayerListChannel Channel(string header, params PlayerRow[] rows)
        => new() { Header = header, Rows = rows };

    private static PlayerRow Row(string name, string? ping = null)
        => new() { DisplayName = name, PingText = ping };

    // ---------------------------------------------------------------- metrics

    [TestMethod]
    public void Metrics_DeriveRowAndPanelWidthFromWidestRowAndPing()
    {
        // name "ABCD" = 40, MiddlePadding = 32 -> 72
        // ping "99ms" = 40, plus spaceWidth 10 -> 50
        // rowWidth = 72 + 50 + 2*4 = 130; panelWidth = 130 + 2*16 = 162
        PlayerListMetrics metrics = PlayerListMetrics.Compute(
            [Channel("H", Row("ABCD", "99ms"))],
            new FakeTextRenderer(),
            scale: 1f,
            lineHeight: LineHeight,
            new PlayerListIcons());

        AssertClose(130f, metrics.RowWidth, "RowWidth");
        AssertClose(162f, metrics.PanelWidth, "PanelWidth");
        AssertClose(50f, metrics.PingColumnWidth, "PingColumnWidth");
        AssertClose(10f, metrics.SpaceWidth, "SpaceWidth");
        AssertClose(10f, metrics.ColonWidth, "ColonWidth");
        AssertClose(28f, metrics.RowHeight, "RowHeight");
        AssertClose(24f, metrics.HeaderHeight, "HeaderHeight");
    }

    [TestMethod]
    public void Metrics_PausedIconContributesItsScaledWidthPlusBothGaps()
    {
        // 10x24 icon scaled to line height 24 -> width 10, plus 2*4 gap = 18
        var icons = new PlayerListIcons { Paused = new FakeTexture(10f, 24f) };
        var row = new PlayerRow { DisplayName = "ABCD", Status = PlayerStatus.Paused };

        PlayerListMetrics metrics = PlayerListMetrics.Compute(
            [Channel("H", row)],
            new FakeTextRenderer(),
            scale: 1f,
            lineHeight: LineHeight,
            icons);

        // maxLine = 40 + 32 + 18 = 90; no pings -> rowWidth = 90 + 0 + 8 = 98
        AssertClose(90f, metrics.RowWidth - 8f, "content width");
        AssertClose(0f, metrics.PingColumnWidth, "PingColumnWidth");
    }

    [TestMethod]
    public void Metrics_HeaderCanDecideThePanelWidth()
    {
        // header "HHHHHHHHHH" = 100 > row content 40 + 32 = 72
        PlayerListMetrics metrics = PlayerListMetrics.Compute(
            [Channel("HHHHHHHHHH", Row("ABCD"))],
            new FakeTextRenderer(),
            scale: 1f,
            lineHeight: LineHeight,
            new PlayerListIcons());

        AssertClose(100f + 0f + 8f, metrics.RowWidth, "RowWidth");
    }

    // ---------------------------------------------------------------- layout

    private static (UiRoot Root, PlayerListPanelNode Panel, AlignNode Host) BuildPanel(
        IReadOnlyList<PlayerListChannel> channels,
        PlayerListIcons icons,
        float viewportWidth = 400f,
        float viewportHeight = 400f)
    {
        var panel = new PlayerListPanelNode(new FakeTextRenderer());
        panel.Rebuild(channels, icons, scale: 1f, lineHeight: LineHeight);

        var host = new AlignNode
        {
            Alignment = UiAlignment.TopLeft,
            Child = panel,
            Style = new UiStyle { Width = viewportWidth, Height = viewportHeight },
        };

        var root = new UiRoot();
        root.SetRoot(host);
        root.Layout(viewportWidth, viewportHeight);
        return (root, panel, host);
    }

    [TestMethod]
    public void Panel_PlacesChannelsAtThePanelMarginAndUsesTheCommonWidth()
    {
        var channels = new List<PlayerListChannel>
        {
            Channel("H", Row("ABCD", "99ms")),
            Channel("H", Row("AB", "7ms")),
        };

        (_, PlayerListPanelNode panel, _) = BuildPanel(channels, new PlayerListIcons());
        PlayerListMetrics metrics = panel.Metrics!;

        var column = (FlexNode)((BoxNode)panel.Child!).Child!;
        AssertClose(2f, column.Children.Count, "channel count");

        UiNode first = column.Children[0];
        UiNode second = column.Children[1];

        // PL.PANEL.MARGIN_X = 16, PL.PANEL.MARGIN_Y = 16
        AssertClose(16f, first.Bounds.X, "channel.X");
        AssertClose(16f, first.Bounds.Y, "channel.Y");
        AssertClose(metrics.PanelWidth, first.Bounds.Width, "channel.Width");
        AssertClose(metrics.PanelWidth, second.Bounds.Width, "second channel.Width");

        // height = 16 pad + lh header + 1 row (lh + 4) + 16 pad
        float expectedChannelHeight = 16f + LineHeight + metrics.RowHeight + 16f;
        AssertClose(expectedChannelHeight, first.Bounds.Height, "channel.Height");

        // spacing between channels is ChannelSpacing
        AssertClose(
            first.Bounds.Bottom + PlayerListLayout.ChannelSpacing,
            second.Bounds.Y,
            "second channel.Y");
    }

    [TestMethod]
    public void Panel_PlacesRowsAtBodyXWithRowWidth()
    {
        var channels = new List<PlayerListChannel> { Channel("H", Row("ABCD", "99ms")) };
        (_, PlayerListPanelNode panel, _) = BuildPanel(channels, new PlayerListIcons());
        PlayerListMetrics metrics = panel.Metrics!;

        var channelNode = (PlayerListChannelNode)((FlexNode)((BoxNode)panel.Child!).Child!).Children[0];
        PlayerRowNode row = channelNode.RowNodes[0];

        // PL.PANEL.BODY_X = 16 + 16 = 32
        AssertClose(32f, row.Bounds.X, "row.X");
        AssertClose(metrics.RowWidth, row.Bounds.Width, "row.Width");
        AssertClose(metrics.RowHeight, row.Bounds.Height, "row.Height");
        AssertClose(16f + 16f + LineHeight, row.Bounds.Y, "row.Y");
    }

    [TestMethod]
    public void Panel_ContentHeightCoversMarginsAndSpacing()
    {
        var channels = new List<PlayerListChannel>
        {
            Channel("H", Row("A")),
            Channel("H", Row("B")),
        };

        (_, PlayerListPanelNode panel, _) = BuildPanel(channels, new PlayerListIcons());

        float channelHeight = 16f + LineHeight + (LineHeight + 4f) + 16f;
        float expected = 16f + channelHeight + PlayerListLayout.ChannelSpacing + channelHeight + 16f;
        AssertClose(expected, panel.ContentSize.Height, "content height");
        AssertClose(0f, panel.MaxScroll, "content fits, so nothing should scroll");
    }

    // ---------------------------------------------------------------- scroll bounds / culling

    [TestMethod]
    public void Q2_ScrollIsBoundedByContentMinusViewport()
    {
        var controller = new PlayerListController
        {
            ContentHeight = 1200f,
            ViewportHeight = 1080f,
        };
        controller.SetOpen(true);

        AssertClose(120f, controller.MaxScroll, "MaxScroll");

        controller.ScrollDownHeld = true;
        for (int i = 0; i < 100; i++)
        {
            controller.Update(0.1f);
        }

        AssertClose(120f, controller.ScrollTarget, "ScrollTarget");
        AssertClose(120f, controller.Scroll, "Scroll");
    }

    [TestMethod]
    public void Q2_ContentShorterThanViewportCannotScroll()
    {
        var controller = new PlayerListController
        {
            ContentHeight = 800f,
            ViewportHeight = 1080f,
        };
        controller.SetOpen(true);
        controller.ScrollDownHeld = true;

        for (int i = 0; i < 20; i++)
        {
            controller.Update(0.1f);
        }

        AssertClose(0f, controller.MaxScroll, "MaxScroll");
        AssertClose(0f, controller.Scroll, "Scroll");
    }

    [TestMethod]
    public void Controller_ClosingResetsScroll()
    {
        var controller = new PlayerListController { ContentHeight = 2000f, ViewportHeight = 1000f };
        controller.SetOpen(true);
        controller.ScrollDownHeld = true;
        for (int i = 0; i < 20; i++)
        {
            controller.Update(0.1f);
        }
        Assert.IsGreaterThan(0f, controller.Scroll, "scroll should have advanced");

        controller.SetOpen(false);
        AssertClose(0f, controller.Scroll, "Scroll after close");
        AssertClose(0f, controller.ScrollTarget, "ScrollTarget after close");
    }

    [TestMethod]
    public void Q3_ChannelsOutsideTheViewportAreNotPainted()
    {
        var channels = new List<PlayerListChannel>
        {
            Channel("A", Row("A")),
            Channel("B", Row("B")),
            Channel("C", Row("C")),
        };

        (UiRoot root, PlayerListPanelNode panel, AlignNode host) = BuildPanel(channels, new PlayerListIcons());

        var canvas = new RecordingCanvas();
        root.Paint(canvas);
        int allVisible = CountChannelBackgrounds(canvas);

        // Scroll the content up so the first two channels leave the viewport.
        panel.Offset = 200f;
        root.Layout(400f, 400f);

        canvas.FillColors.Clear();
        root.Paint(canvas);
        int culled = CountChannelBackgrounds(canvas);

        _ = host;
        AssertClose(3f, allVisible, "channels painted before scrolling");
        AssertClose(1f, culled, "channels painted after scrolling");
    }

    private static int CountChannelBackgrounds(RecordingCanvas canvas)
    {
        UiColor background = MiaoNetUiTheme.PlayerList.Background;
        int count = 0;
        foreach (UiColor color in canvas.FillColors)
        {
            if (color == background)
            {
                count++;
            }
        }
        return count;
    }
}
