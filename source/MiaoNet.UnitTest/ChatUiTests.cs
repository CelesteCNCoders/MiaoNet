using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.Chat;
using Celeste.Mod.MiaoNet.UI.Chat;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace MiaoNet.UnitTest;

// chat list stuff: the stacked screen geometry, the scroll clamp, fade timers and virtual-list
// windowing.
[TestClass]
public sealed class ChatUiTests
{
    private const float LineHeight = 24f;
    private const float CharWidth = 10f;
    private const float ScreenHeight = 1080f;
    private const float ScreenWidth = 1920f;

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

    private sealed class FakeChatSource : IChatMessageSource
    {
        private readonly object[] keys;
        private readonly bool withTime;

        public FakeChatSource(int count, bool withTime = false)
        {
            Count = count;
            this.withTime = withTime;
            keys = new object[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = new object();
            }
        }

        public int Count { get; }

        public object GetKey(int index) => keys[index];

        public ChatMessageRow BuildRow(int index) => new()
        {
            StableKey = keys[index],
            TimeText = withTime ? "00:00:00" : null,
            Runs = [new ChatTextRun("msg", UiColor.White, TextDecoration.None)],
        };
    }

    private static ChatScreenNode BuildChatScreen(
        ChatListController controller,
        IChatMessageSource? source,
        bool active,
        float messagePadding,
        float backgroundOpacity = 0.5f,
        float textOpacity = 1f,
        float scale = 1f)
    {
        float rowHeight = LineHeight + (2f * messagePadding);

        var messages = new ChatMessageListNode(new FakeTextRenderer(), controller)
        {
            LineHeight = LineHeight,
            MessagePaddingY = messagePadding,
            Scale = scale,
            BackgroundOpacity = backgroundOpacity,
            TextOpacity = textOpacity,
        };
        messages.ItemExtent = rowHeight;
        messages.SetMessages(source);

        var tabs = new ChatTabBarNode(new FakeTextRenderer())
        {
            InitialTitle = "Global",
            LineHeight = LineHeight,
            Scale = 1f,
        };

        var field = new TextFieldNode(
            new FakeTextRenderer(),
            new TextEditingController(new NoopCompletionProvider()))
        { LineHeight = LineHeight, Scale = 1f };

        var input = new ChatInputNode(
            field,
            new CompletionPopupNode(new FakeTextRenderer()))
        { LineHeight = LineHeight, Scale = 1f };

        return new ChatScreenNode(messages, tabs, input)
        {
            Active = active,
            LineHeight = LineHeight,
            IdleRatio = 0.4f,
            ActiveRatio = 0.8f,
        };
    }

    private sealed class NoopCompletionProvider : ICompletionProvider
    {
        public IEnumerable<Completion>? GetCompletions(string input) => null;
    }

    // ---------------------------------------------------------------- geometry

    [TestMethod]
    public void ScreenGeometry_MatchesTheParameterSpecStackOfAnchors()
    {
        var controller = new ChatListController();
        ChatScreenNode screen = BuildChatScreen(controller, new FakeChatSource(0), active: false, messagePadding: 4f);

        // CHAT.LIST.INPUT_TOP = H - 16 - lh - 16
        AssertClose(1024f, screen.InputTopY(ScreenHeight), "InputTopY");
        // CHAT.TAB.BASE = INPUT_TOP - 8
        AssertClose(1016f, screen.TabsBottomY(ScreenHeight), "TabsBottomY");
        AssertClose(992f, screen.TabsTopY(ScreenHeight), "TabsTopY");
        // CHAT.LIST.BASE_Y, idle: the input box top.
        AssertClose(1024f, screen.BaseY(ScreenHeight), "idle BaseY");
    }

    [TestMethod]
    public void ScreenGeometry_ActiveShiftsBaselineToTheTabTop()
    {
        var controller = new ChatListController();
        ChatScreenNode screen = BuildChatScreen(controller, new FakeChatSource(0), active: true, messagePadding: 4f);

        // CHAT.LIST.TAB_TOP = INPUT_TOP - lh - 16 = 984
        AssertClose(984f, screen.BaseY(ScreenHeight), "active BaseY");
    }

    [TestMethod]
    public void ScreenGeometry_MaxHeightIsFlooredToWholeRows()
    {
        var controller = new ChatListController();
        ChatScreenNode idle = BuildChatScreen(controller, new FakeChatSource(0), active: false, messagePadding: 4f);
        ChatScreenNode active = BuildChatScreen(controller, new FakeChatSource(0), active: true, messagePadding: 4f);

        // floor(0.4 * 1024 / 32) * 32 = 384
        AssertClose(384f, idle.ListHeight(ScreenHeight), "idle ListHeight");
        // floor(0.8 * 984 / 32) * 32 = 768
        AssertClose(768f, active.ListHeight(ScreenHeight), "active ListHeight");
    }

    // ---------------------------------------------------------------- scroll clamp

    [TestMethod]
    public void Q1_ScrollClampUsesTheSameExtentAsRendering()
    {
        var controller = new ChatListController
        {
            ItemExtent = 32f,       // lh 24 + 2 * ChatMessagePadding 4
            MessageCount = 20,
            ViewportHeight = 384f,
        };

        // Both the clamp and the content height use mh = 32: 20*32 - 384 = 256.
        AssertClose(640f, controller.ContentHeight, "ContentHeight");
        AssertClose(256f, controller.MaxScroll, "MaxScroll");

        controller.ScrollBy(10_000f);
        AssertClose(256f, controller.ScrollTarget, "ScrollTarget is clamped");
    }

    [TestMethod]
    public void Q1_PaddingZeroShrinksTheContentAndTheClampTogether()
    {
        var controller = new ChatListController
        {
            ItemExtent = 24f,       // lh 24 + 2 * padding 0
            MessageCount = 20,
            ViewportHeight = 384f,
        };

        AssertClose(480f, controller.ContentHeight, "ContentHeight");
        AssertClose(96f, controller.MaxScroll, "MaxScroll");
    }

    [TestMethod]
    public void Scroll_ContentOffsetIsMeasuredFromTheBottom()
    {
        var controller = new ChatListController
        {
            ItemExtent = 32f,
            MessageCount = 20,
            ViewportHeight = 384f,
        };

        // scroll 0 means the newest message sits at the baseline, so the top-down offset is MaxScroll
        AssertClose(256f, controller.ContentOffset, "ContentOffset at scroll 0");

        controller.ScrollBy(100f);
        controller.UpdateScroll(10f);
        AssertClose(100f, controller.Scroll, "Scroll");
        AssertClose(156f, controller.ContentOffset, "ContentOffset after scrolling back");
    }

    // ---------------------------------------------------------------- fade timers

    [TestMethod]
    public void FadeTimers_MessageFadesOutAfterItsShowDuration()
    {
        var controller = new ChatListController { ShowDuration = 8f };
        var key = new object();

        controller.UpdateTimers([key], [1], 1f);
        AssertClose(1f, controller.FadeOf(key), "still showing");

        // Past the show duration the message starts fading over ChatLayout.DisappearDuration.
        controller.UpdateTimers([key], [1], 8f);
        controller.UpdateTimers([key], [1], ChatLayout.DisappearDuration / 2f);
        Assert.IsGreaterThan(0f, controller.FadeOf(key), "half way through the fade");
        Assert.IsLessThan(1f, controller.FadeOf(key), "half way through the fade");

        controller.UpdateTimers([key], [1], ChatLayout.DisappearDuration);
        AssertClose(0f, controller.FadeOf(key), "faded out");
    }

    [TestMethod]
    public void FadeTimers_ActiveChatNeverFades()
    {
        var controller = new ChatListController { Active = true, ShowDuration = 8f };
        var key = new object();

        controller.UpdateTimers([key], [1], 1f);
        controller.UpdateTimers([key], [1], 100f);

        AssertClose(1f, controller.FadeOf(key), "active messages stay opaque");
    }

    [TestMethod]
    public void FadeTimers_FoldedMessagePlaysTheCounterPop()
    {
        var controller = new ChatListController { ShowDuration = 8f };
        var key = new object();

        controller.UpdateTimers([key], [3], 0.1f);
        Assert.IsLessThan(1f, controller.CounterPopProgressOf(key), "pop animation is playing");

        controller.UpdateTimers([key], [3], Celeste.Mod.MiaoNet.Chat.FoldCounter.PopDuration);
        AssertClose(1f, controller.CounterPopProgressOf(key), "pop animation finished");
    }

    [TestMethod]
    public void FadeTimers_StaleStatesAreSwept()
    {
        var controller = new ChatListController { ShowDuration = 8f };
        var first = new object();
        var second = new object();

        controller.UpdateTimers([first, second], [1, 1], 0.1f);
        // the folded message is gone from the log, so its state shouldn't linger
        controller.UpdateTimers([second], [1], 0.1f);
        controller.UpdateTimers([second], [1], 0.1f);

        // A fresh state for a re-added key starts fully opaque again.
        controller.UpdateTimers([first, second], [1, 1], 0.1f);
        AssertClose(1f, controller.FadeOf(first), "re-added key restarts its timer");
    }

    // ---------------------------------------------------------------- message row

    private static ChatMessageRow Row(int characters, string? time = null) => new()
    {
        StableKey = new object(),
        TimeText = time,
        Runs = [new ChatTextRun(new string('m', characters), UiColor.White, TextDecoration.None)],
    };

    private static ChatMessageNode MeasureRow(ChatMessageRow row, float messagePadding)
    {
        var node = new ChatMessageNode(new FakeTextRenderer(), row)
        {
            LineHeight = LineHeight,
            MessagePaddingY = messagePadding,
            Scale = 1f,
        };
        node.Measure(new BoxConstraints(0f, float.PositiveInfinity, node.MessageLineHeight, node.MessageLineHeight));
        return node;
    }

    [TestMethod]
    public void MessageRow_LineHeightFollowsThePaddingSetting()
    {
        // B1 / B2: mh = lh + 2 * padding
        AssertClose(32f, MeasureRow(Row(1), 4f).MessageLineHeight, "B1 padding 4");
        AssertClose(24f, MeasureRow(Row(1), 0f).MessageLineHeight, "B2 padding 0");
    }

    [TestMethod]
    public void MessageRow_WidthIsTextPlusBothHorizontalPaddings()
    {
        // B3: 10 characters * 10 + 2 * 8 = 116
        AssertClose(116f, MeasureRow(Row(10), 4f).MeasuredSize.Width, "B3 background width");
    }

    [TestMethod]
    public void MessageRow_ReservesAFixedTimestampCell()
    {
        // B6: 3.5625 * lh + 2 * 2 = 89.5
        ChatMessageNode node = MeasureRow(Row(10, "00:00:00"), 4f);
        AssertClose(89.5f, node.TimeCellWidth, "B6 time cell width");
        AssertClose(116f + 89.5f, node.MeasuredSize.Width, "cell shifts the text column");
    }

    [TestMethod]
    public void MessageRow_BackgroundIsPixelSnappedToItsBounds()
    {
        var canvas = new RecordingCanvas();
        ChatMessageNode node = MeasureRow(Row(10), 4f);
        node.Arrange(new UiRect(10.4f, 20.6f, 30.3f, 32f));

        node.PaintTree(canvas, 1f);

        Assert.HasCount(1, canvas.Fills, "one background fill");
        UiRect rect = canvas.Fills[0];
        AssertClose(10f, rect.X, "snapped X");
        AssertClose(20f, rect.Y, "snapped Y");
        AssertClose(30f, rect.Width, "snapped width");
        AssertClose(32f, rect.Height, "snapped height");
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

    // ---------------------------------------------------------------- regressions

    [TestMethod]
    public void ShortLog_IsBottomAlignedLikeTheOriginal()
    {
        var controller = new ChatListController();
        var source = new FakeChatSource(3);          // 3 * 32 = 96, well under the 384 viewport
        ChatScreenNode screen = BuildChatScreen(controller, source, active: false, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        LayoutWithController(screen, controller, ui, source.Count);

        AssertClose(96f, controller.ContentHeight, "content height");
        AssertClose(-288f, controller.ContentOffset, "offset goes negative for a short log");
        AssertClose(0f, controller.MaxScroll, "a short log still cannot scroll");

        ChatMessageListNode list = screen.Messages;
        UiNode newest = list.Children[list.Children.Count - 1];
        AssertClose(list.Bounds.Bottom, newest.Bounds.Bottom, "the newest row ends on the list bottom");
    }

    [TestMethod]
    public void InactiveChat_PaintsNeitherTabsNorTheInputBox()
    {
        var controller = new ChatListController();
        var source = new FakeChatSource(3);
        ChatScreenNode screen = BuildChatScreen(controller, source, active: false, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        LayoutWithController(screen, controller, ui, source.Count);

        var canvas = new RecordingCanvas();
        ui.Paint(canvas);

        Assert.IsFalse(screen.Tabs.IsVisible, "tab strip hidden while the chat is closed");
        Assert.IsFalse(screen.Input.IsVisible, "input box hidden while the chat is closed");
        Assert.DoesNotContain(
            MiaoNetUiTheme.Input.Background,
            canvas.FillColors,
            "the input box background is not painted while closed");
    }

    [TestMethod]
    public void ActiveChat_PaintsTabsAndTheInputBox()
    {
        var controller = new ChatListController();
        var source = new FakeChatSource(3);
        ChatScreenNode screen = BuildChatScreen(controller, source, active: true, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        LayoutWithController(screen, controller, ui, source.Count);

        var canvas = new RecordingCanvas();
        ui.Paint(canvas);

        Assert.IsTrue(screen.Tabs.IsVisible, "tab strip visible while the chat is open");
        Assert.IsTrue(screen.Input.IsVisible, "input box visible while the chat is open");
        Assert.Contains(
            MiaoNetUiTheme.Input.Background,
            canvas.FillColors,
            "the input box background is painted while open");
    }

    [TestMethod]
    public void VirtualList_AppliesItemMetricsBeforeTheFirstMeasurement()
    {
        var controller = new ChatListController();
        var source = new FakeChatSource(3, withTime: true);
        ChatScreenNode screen = BuildChatScreen(
            controller,
            source,
            active: false,
            messagePadding: 4f,
            scale: 0.5f);

        var ui = new UiRoot();
        ui.SetRoot(screen);

        // Only ONE layout pass: the nodes are created during it, so this catches a first-frame
        // measurement that still used the node type's defaults (LineHeight 0, Scale 1).
        ui.Layout(ScreenWidth, ScreenHeight);

        ChatMessageListNode list = screen.Messages;
        // time cell 3.5625 * 24 + 4 = 89.5; "msg" 3 chars * 10 * 0.5 = 15; padding 2 * 8 = 16
        const float expected = 89.5f + 15f + 16f;
        AssertClose(expected, list.Children[0].Bounds.Width, "first layout already uses the real metrics");

        ui.Layout(ScreenWidth, ScreenHeight);
        AssertClose(expected, list.Children[0].Bounds.Width, "width is stable on the next pass");
    }

    // ---------------------------------------------------------------- virtual list

    // layout, then sync the controller the way the host does: item extent, message count and
    // viewport height, then the top-down offset derived from the bottom scroll.
    private static void LayoutWithController(ChatScreenNode screen, ChatListController controller, UiRoot ui, int messageCount)
    {
        ui.Layout(ScreenWidth, ScreenHeight);
        controller.ItemExtent = screen.Messages.MessageLineHeight;
        controller.MessageCount = messageCount;
        controller.ViewportHeight = screen.Messages.Bounds.Height;
        screen.Messages.Offset = controller.ContentOffset;
        ui.Layout(ScreenWidth, ScreenHeight);
    }

    [TestMethod]
    public void VirtualList_MountsOnlyTheVisibleWindowPlusOverscan()
    {
        var controller = new ChatListController();
        var source = new FakeChatSource(100);
        ChatScreenNode screen = BuildChatScreen(controller, source, active: false, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        LayoutWithController(screen, controller, ui, source.Count);

        ChatMessageListNode list = screen.Messages;
        AssertClose(384f, list.Bounds.Height, "list height");

        // 384 / 32 = 12 visible rows, plus one overscan row on each side.
        Assert.IsLessThanOrEqualTo(14, list.Children.Count, "mounted rows");
        Assert.IsGreaterThanOrEqualTo(12, list.Children.Count, "mounted rows");
    }

    [TestMethod]
    public void VirtualList_AtRestShowsTheNewestMessages()
    {
        var controller = new ChatListController();
        var source = new FakeChatSource(100);
        ChatScreenNode screen = BuildChatScreen(controller, source, active: false, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        LayoutWithController(screen, controller, ui, source.Count);

        ChatMessageListNode list = screen.Messages;
        Assert.AreEqual(99, list.LastVisibleIndex, "the newest message is visible at rest");
        Assert.IsGreaterThanOrEqualTo(0, list.FirstVisibleIndex, "some older message is visible");
        Assert.IsLessThan(99, list.FirstVisibleIndex, "the window is wider than one row");
    }

    [TestMethod]
    public void VirtualList_ScrollingBackMovesTheWindowIntoHistory()
    {
        var controller = new ChatListController
        {
            Active = true,
            ItemExtent = 32f,
            MessageCount = 100,
        };
        var source = new FakeChatSource(100);
        ChatScreenNode screen = BuildChatScreen(controller, source, active: true, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        ui.Layout(ScreenWidth, ScreenHeight);

        // the active list is 768 tall, so that's the viewport the controller has to be told
        controller.ViewportHeight = screen.Messages.Bounds.Height;
        controller.ScrollBy(320f);
        controller.UpdateScroll(10f);
        screen.Messages.Offset = controller.ContentOffset;

        ui.Layout(ScreenWidth, ScreenHeight);

        ChatMessageListNode list = screen.Messages;
        AssertClose(768f, list.Bounds.Height, "active list height");
        Assert.IsLessThan(99, list.LastVisibleIndex, "the newest message scrolled out of view");
    }

    // ---------------------------------------------------------------- tab switching

    // the content height and the clamp follow the displayed list, so switching to a tab with fewer
    // messages re-derives both from the displayed list count, not the full log.
    [TestMethod]
    public void TabSwitch_RecomputesTheClampFromTheDisplayedList()
    {
        var controller = new ChatListController { Active = true };
        var fullLog = new FakeChatSource(100);
        ChatScreenNode screen = BuildChatScreen(controller, fullLog, active: true, messagePadding: 4f);

        var ui = new UiRoot();
        ui.SetRoot(screen);
        SyncWithController(screen, controller, ui);

        AssertClose(100f * controller.ItemExtent, controller.ContentHeight, "content height of the full log");
        controller.ScrollBy(10_000f);
        controller.UpdateScroll(10f);
        Assert.IsGreaterThan(0f, controller.Scroll, "scrolled back into history");

        // The tab switch swaps in a much shorter displayed list.
        var shortTab = new FakeChatSource(3);
        screen.Messages.SetMessages(shortTab);
        SyncWithController(screen, controller, ui);

        AssertClose(3f * controller.ItemExtent, controller.ContentHeight, "content height now follows the tab");
        AssertClose(0f, controller.MaxScroll, "a tab shorter than the viewport cannot scroll");
        AssertClose(0f, controller.ScrollTarget, "and the scroll target is pulled back to the bottom");
    }

    // the count the host feeds the controller comes from the message list node; it's the only thing
    // that knows the displayed list, never the full log the fade timers walk.
    [TestMethod]
    public void MessageListNode_ReportsTheDisplayedCountNotTheFullLog()
    {
        var controller = new ChatListController();
        ChatScreenNode screen = BuildChatScreen(controller, new FakeChatSource(100), active: true, messagePadding: 4f);

        Assert.AreEqual(100, screen.Messages.MessageCount, "the mounted source is the displayed list");
        Assert.AreEqual(100, controller.MessageCount, "and the controller was told the same number");

        screen.Messages.SetMessages(new FakeChatSource(7));

        Assert.AreEqual(7, screen.Messages.MessageCount, "and it reports whatever was last mounted");
        Assert.AreEqual(7, controller.MessageCount, "which the controller follows");
    }

    // ---------------------------------------------------------------- scroll reset on close

    [TestMethod]
    public void ClosingTheChatResetsScrollToTheBottom()
    {
        var controller = new ChatListController
        {
            Active = true,
            ItemExtent = 32f,
            MessageCount = 100,
            ViewportHeight = 384f,
        };

        controller.ScrollBy(500f);
        controller.UpdateScroll(10f);
        Assert.IsGreaterThan(0f, controller.Scroll, "scrolled back while the chat is open");

        controller.Active = false;

        AssertClose(0f, controller.Scroll, "Scroll is back at the bottom");
        AssertClose(0f, controller.ScrollTarget, "and so is the target");
    }

    [TestMethod]
    public void OpeningTheChatKeepsTheScrollPosition()
    {
        var controller = new ChatListController
        {
            ItemExtent = 32f,
            MessageCount = 100,
            ViewportHeight = 384f,
        };

        controller.ScrollBy(500f);
        controller.UpdateScroll(10f);
        float scrolled = controller.Scroll;

        controller.Active = true;

        AssertClose(scrolled, controller.Scroll, "only closing resets the scroll");
    }

    // layout and sync the controller the way the host does. no message count here on purpose:
    // SetMessages pushes it.
    private static void SyncWithController(ChatScreenNode screen, ChatListController controller, UiRoot ui)
    {
        ui.Layout(ScreenWidth, ScreenHeight);
        controller.ItemExtent = screen.Messages.MessageLineHeight;
        controller.ViewportHeight = screen.Messages.Bounds.Height;
        controller.UpdateScroll(0f);
        screen.Messages.Offset = controller.ContentOffset;
        ui.Layout(ScreenWidth, ScreenHeight);
    }
}
