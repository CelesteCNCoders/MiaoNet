using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.MiaoNet.UI.Input;

namespace MiaoNet.UnitTest;

// the arbitration contract of UIInputRouter. these tests watch routing the way a consumer does:
// register reactions and see which ones fire. nothing reads a routing result directly, so a
// reaction that stops being delivered fails even if the table still looks fine.
[TestClass]
public sealed class UiInputRouterTests
{
    private static readonly UIInputAction[] ChatEditingActions =
    [
        UIInputAction.Submit,
        UIInputAction.Cancel,
        UIInputAction.ChannelPrevious,
        UIInputAction.ChannelNext,
        UIInputAction.HistoryUp,
        UIInputAction.HistoryDown,
        UIInputAction.CompletionUp,
        UIInputAction.CompletionDown,
        UIInputAction.CaretLeft,
        UIInputAction.CaretRight,
        UIInputAction.CompletionAccept,
        UIInputAction.Paste,
    ];

    private static readonly UIInputAction[] AllActions = Enum.GetValues<UIInputAction>();

    // a router with both consumers claiming everything, recording what actually fires
    private sealed class Harness
    {
        public readonly UIInputRouter Router = new();
        public readonly UIInputFrame Frame = new();
        public readonly List<UIInputAction> ChatFired = [];
        public readonly List<UIInputAction> PlayerListFired = [];
        public readonly UIInputRegistrations Chat;
        public readonly UIInputRegistrations PlayerList;

        private readonly HashSet<UIInputAction> chatHeld = [];
        private readonly HashSet<UIInputAction> playerListHeld = [];

        public Harness(UIFocusOwner focus)
        {
            Router.SetFocus(focus);
            Chat = Router.Register(UIInputConsumer.Chat);
            PlayerList = Router.Register(UIInputConsumer.PlayerList);

            foreach (UIInputRule rule in UIInputRouter.Rules)
            {
                bool isChat = OwnedByChat(rule.Action);
                UIInputRegistrations set = isChat ? Chat : PlayerList;
                List<UIInputAction> log = isChat ? ChatFired : PlayerListFired;

                set.On(rule.Action, () => log.Add(rule.Action));
                set.OwnHeld(rule.Action);
                (isChat ? chatHeld : playerListHeld).Add(rule.Action);
            }
        }

        // routes one action and reports which consumer it reached
        public (bool Chat, bool PlayerList) RouteOne(UIInputAction action, bool held = false)
        {
            Frame.Clear();
            ChatFired.Clear();
            PlayerListFired.Clear();

            if (held)
            {
                Frame.Hold(action);
            }
            else
            {
                Frame.Press(action);
            }

            Router.Route(Frame);

            // edges show up through the reaction; a held level has none to watch, and only the
            // owning consumer can be asked about it.
            if (!held)
            {
                return (ChatFired.Contains(action), PlayerListFired.Contains(action));
            }

            return (chatHeld.Contains(action) && Chat.IsHeld(action),
                    playerListHeld.Contains(action) && PlayerList.IsHeld(action));
        }
    }

    // ownership lives with the registration now, so the harness has to say who owns what. this
    // mirrors what the real components claim; Seal is what checks the real one.
    private static bool OwnedByChat(UIInputAction action) => action switch
    {
        UIInputAction.PlayerListToggle => false,
        UIInputAction.PlayerListScrollUp => false,
        UIInputAction.PlayerListScrollDown => false,
        _ => true,
    };

    // ---------------------------------------------------------------- the table itself

    [TestMethod]
    public void Rules_CoverEveryActionExactlyOnce()
    {
        var duplicates = UIInputRouter.Rules
            .GroupBy(rule => rule.Action)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.HasCount(0, duplicates, "an action routed twice would reach two consumers");

        var missing = AllActions.Except(UIInputRouter.Rules.Select(rule => rule.Action)).ToArray();
        Assert.HasCount(0, missing, "every action needs a rule, or it is silently dropped");

        var unknown = UIInputRouter.Rules.Select(rule => rule.Action).Except(AllActions).ToArray();
        Assert.HasCount(0, unknown, "a rule for a non-existent action is dead weight");
    }

    [TestMethod]
    public void Rules_RejectAnEmptyFocusList()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new UIInputRule(UIInputAction.Submit));
    }

    // ---------------------------------------------------------------- one consumer only

    [TestMethod]
    public void NoActionEverReachesTwoConsumers()
    {
        foreach (UIFocusOwner focus in Enum.GetValues<UIFocusOwner>())
        {
            var harness = new Harness(focus);
            foreach (UIInputAction action in AllActions)
            {
                (bool chat, bool playerList) = harness.RouteOne(action);
                Assert.IsFalse(
                    chat && playerList,
                    $"{action} reached both consumers with focus {focus}");
            }
        }
    }

    // ---------------------------------------------------------------- tab / completion

    [TestMethod]
    public void Tab_GoesToCompletionWhenTheChatOwnsFocus()
    {
        var harness = new Harness(UIFocusOwner.Chat);
        Assert.IsTrue(
            harness.RouteOne(UIInputAction.CompletionAccept).Chat,
            "the chat got the completion accept");

        // the list binding is the same physical key, so it shouldn't see it too
        (bool chat, bool playerList) = harness.RouteOne(UIInputAction.PlayerListToggle);
        Assert.IsFalse(playerList, "the list must not see Tab while chatting");
        Assert.IsFalse(chat, "and the chat does not treat it as a list toggle");
    }

    [TestMethod]
    public void Tab_OpensTheListOnlyFromTheNeutralState()
    {
        Assert.IsTrue(
            new Harness(UIFocusOwner.None).RouteOne(UIInputAction.PlayerListToggle).PlayerList,
            "neutral focus opens the list");

        Assert.IsTrue(
            new Harness(UIFocusOwner.PlayerList).RouteOne(UIInputAction.PlayerListToggle).PlayerList,
            "and it can be closed again from the list");
    }

    // ---------------------------------------------------------------- focus

    [TestMethod]
    public void Focus_IsExplicitAndReportedAsTheOldFlag()
    {
        var router = new UIInputRouter();
        Assert.AreEqual(UIFocusOwner.None, router.Focus);
        Assert.IsFalse(router.HasFocus, "no owner means no focus");

        router.SetFocus(UIFocusOwner.Chat);
        Assert.IsTrue(router.HasFocus);

        router.SetFocus(UIFocusOwner.PlayerList);
        Assert.IsTrue(router.HasFocus);
        Assert.AreEqual(UIFocusOwner.PlayerList, router.Focus);

        router.SetFocus(UIFocusOwner.None);
        Assert.IsFalse(router.HasFocus);
    }

    // ---------------------------------------------------------------- focus gating

    [TestMethod]
    public void OpenActions_ApplyOnlyFromTheNeutralState()
    {
        foreach (UIInputAction action in (UIInputAction[])[UIInputAction.ChatToggle, UIInputAction.ChatCommandToggle])
        {
            Assert.IsTrue(new Harness(UIFocusOwner.None).RouteOne(action).Chat, $"{action} from neutral");

            Assert.IsFalse(
                new Harness(UIFocusOwner.Chat).RouteOne(action).Chat,
                $"{action} is inert while chatting");
            Assert.IsFalse(
                new Harness(UIFocusOwner.PlayerList).RouteOne(action).Chat,
                $"{action} is inert while the list is open");
        }
    }

    [TestMethod]
    public void ChatEditingActions_RequireChatFocus()
    {
        foreach (UIInputAction action in ChatEditingActions)
        {
            Assert.IsTrue(
                new Harness(UIFocusOwner.Chat).RouteOne(action).Chat,
                $"{action} reaches the chat when it owns focus");

            Assert.IsFalse(
                new Harness(UIFocusOwner.None).RouteOne(action).Chat,
                $"{action} is inert with no focus");

            Assert.IsFalse(
                new Harness(UIFocusOwner.PlayerList).RouteOne(action).Chat,
                $"{action} is inert while the list is open");
        }
    }

    [TestMethod]
    public void PlayerListScroll_RequiresPlayerListFocus()
    {
        foreach (UIInputAction action in (UIInputAction[])[UIInputAction.PlayerListScrollUp, UIInputAction.PlayerListScrollDown])
        {
            Assert.IsTrue(
                new Harness(UIFocusOwner.PlayerList).RouteOne(action, held: true).PlayerList,
                $"{action} reaches the list when it owns focus");

            Assert.IsFalse(
                new Harness(UIFocusOwner.None).RouteOne(action, held: true).PlayerList,
                $"{action} is inert with no focus");
        }
    }

    [TestMethod]
    public void HeldPlayerListToggle_IsNotDeliveredWhileTheChatOwnsFocus()
    {
        Assert.IsTrue(
            new Harness(UIFocusOwner.None).RouteOne(UIInputAction.PlayerListToggle, held: true).PlayerList,
            "hold mode works from the neutral state");

        Assert.IsFalse(
            new Harness(UIFocusOwner.Chat).RouteOne(UIInputAction.PlayerListToggle, held: true).PlayerList,
            "and never while the chat owns the keyboard");
    }

    // ---------------------------------------------------------------- chat scroll

    [TestMethod]
    public void ChatScroll_IsOwnedByTheChatListEverywhereExceptThePlayerList()
    {
        static float Route(UIFocusOwner focus)
        {
            var router = new UIInputRouter();
            router.SetFocus(focus);
            router.Route(new UIInputFrame { ChatScrollDelta = 120f });
            return router.ChatScrollDelta;
        }

        Assert.AreEqual(120f, Route(UIFocusOwner.None), "scrolling works while walking");
        Assert.AreEqual(120f, Route(UIFocusOwner.Chat), "and while chatting");
        Assert.AreEqual(0f, Route(UIFocusOwner.PlayerList), "but not while the list owns the keyboard");
    }

    [TestMethod]
    public void ChatScrollDelta_DoesNotLeakBetweenFrames()
    {
        var router = new UIInputRouter();
        router.Route(new UIInputFrame { ChatScrollDelta = 50f });
        Assert.AreEqual(50f, router.ChatScrollDelta);

        router.Route(new UIInputFrame());
        Assert.AreEqual(0f, router.ChatScrollDelta, "the previous wheel delta is gone");
    }

    [TestMethod]
    public void ChatListPaging_IsDeliveredAsAHeldLevel()
    {
        var open = new Harness(UIFocusOwner.None);
        open.RouteOne(UIInputAction.ChatListScrollUp, held: true);
        Assert.IsTrue(open.Chat.IsHeld(UIInputAction.ChatListScrollUp), "PageUp is a held key");

        var blocked = new Harness(UIFocusOwner.PlayerList);
        blocked.RouteOne(UIInputAction.ChatListScrollUp, held: true);
        Assert.IsFalse(blocked.Chat.IsHeld(UIInputAction.ChatListScrollUp), "but not while the list is open");
    }

    // ---------------------------------------------------------------- routing is a pure function

    [TestMethod]
    public void Routing_IsAFunctionOfFrameAndFocusOnly()
    {
        // routing the same frame twice with the same focus has to give the same answer, regardless
        // of the caller: delivery depends only on the frame and the focus owner.
        var harness = new Harness(UIFocusOwner.None);
        harness.Frame.Press(UIInputAction.CompletionAccept);
        harness.Frame.Press(UIInputAction.PlayerListToggle);
        harness.Frame.Press(UIInputAction.ChatToggle);

        harness.Router.Route(harness.Frame);
        var first = (harness.ChatFired.ToArray(), harness.PlayerListFired.ToArray());

        harness.ChatFired.Clear();
        harness.PlayerListFired.Clear();
        harness.Router.Route(harness.Frame);

        CollectionAssert.AreEqual(first.Item1, harness.ChatFired.ToArray(), "chat delivery is stable");
        CollectionAssert.AreEqual(first.Item2, harness.PlayerListFired.ToArray(), "list delivery is stable");
    }

    // ---------------------------------------------------------------- focus handover

    [TestMethod]
    public void Route_StopsAfterAReactionTakesOrReleasesTheKeyboard()
    {
        // a reaction that drops focus has to stop the rest of the frame acting on a closed panel.
        var router = new UIInputRouter();
        router.SetFocus(UIFocusOwner.Chat);

        UIInputRegistrations chat = router.Register(UIInputConsumer.Chat);
        var fired = new List<UIInputAction>();
        chat.On(UIInputAction.Cancel, () =>
        {
            fired.Add(UIInputAction.Cancel);
            router.SetFocus(UIFocusOwner.None);
        });
        chat.On(UIInputAction.Paste, () => fired.Add(UIInputAction.Paste));

        var frame = new UIInputFrame();
        frame.Press(UIInputAction.Cancel);
        frame.Press(UIInputAction.Paste);
        router.Route(frame);

        CollectionAssert.AreEqual(
            new[] { UIInputAction.Cancel },
            fired.ToArray(),
            "Cancel closed the box, so Paste must not have run");
    }

    // ---------------------------------------------------------------- registration contract

    [TestMethod]
    public void Register_IsIdempotentPerConsumer()
    {
        var router = new UIInputRouter();
        Assert.AreSame(router.Register(UIInputConsumer.Chat), router.Register(UIInputConsumer.Chat));
        Assert.AreNotSame(router.Register(UIInputConsumer.Chat), router.Register(UIInputConsumer.PlayerList));
    }

    [TestMethod]
    public void On_RejectsASecondReactionToTheSameAction()
    {
        var router = new UIInputRouter();
        UIInputRegistrations chat = router.Register(UIInputConsumer.Chat);
        chat.On(UIInputAction.Submit, () => { });

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.On(UIInputAction.Submit, () => { }));
        Assert.Contains("already claimed", error.Message);
    }

    [TestMethod]
    public void Seal_RejectsTwoConsumersClaimingTheSameAction()
    {
        // ownership comes from the registration, so nothing stops the second claim on the spot; the
        // table is what has to notice, because one action may only ever have one owner.
        var router = new UIInputRouter();
        router.Register(UIInputConsumer.Chat).On(UIInputAction.PlayerListToggle, () => { });
        router.Register(UIInputConsumer.PlayerList).OwnHeld(UIInputAction.PlayerListToggle);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(router.Seal);
        Assert.Contains("claimed by both", error.Message);
    }

    [TestMethod]
    public void IsHeld_RejectsAPollWithoutAClaim()
    {
        var router = new UIInputRouter();
        UIInputRegistrations chat = router.Register(UIInputConsumer.Chat);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.IsHeld(UIInputAction.ChatListScrollUp));
        Assert.Contains("OwnHeld", error.Message);
    }

    [TestMethod]
    public void Seal_AcceptsARouterWhereEverythingIsClaimed()
    {
        new Harness(UIFocusOwner.None).Router.Seal();
    }

    [TestMethod]
    public void Seal_RejectsAnActionNobodyClaimed()
    {
        // The bug this guards against: a rule the router happily routes but no component reads.
        var router = new UIInputRouter();
        router.Register(UIInputConsumer.Chat).On(UIInputAction.Submit, () => { });

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(router.Seal);
        Assert.Contains("nothing claimed it", error.Message);
        Assert.Contains(nameof(UIInputAction.Paste), error.Message, "the unclaimed action is named");
    }

    [TestMethod]
    public void Seal_RejectsRegistrationAfterItHasRun()
    {
        UIInputRouter router = new Harness(UIFocusOwner.None).Router;
        router.Seal();

        Assert.ThrowsExactly<InvalidOperationException>(() => router.Register(UIInputConsumer.Chat));
    }

    // ---------------------------------------------------------------- frame

    [TestMethod]
    public void Frame_ClearResetsEverything()
    {
        var frame = new UIInputFrame();
        frame.Press(UIInputAction.Submit);
        frame.Hold(UIInputAction.PlayerListToggle);
        frame.ChatScrollDelta = 10f;

        frame.Clear();

        Assert.HasCount(0, (IReadOnlyCollection<UIInputAction>)frame.Pressed);
        Assert.HasCount(0, (IReadOnlyCollection<UIInputAction>)frame.Held);
        Assert.AreEqual(0f, frame.ChatScrollDelta);
    }
}
