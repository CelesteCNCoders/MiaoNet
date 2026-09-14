using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.MiaoNet.UI.Input;

namespace MiaoNet.UnitTest;

// the arbitration contract of UiInputRouter. these tests watch routing the way a consumer does:
// register reactions and see which ones fire. nothing reads a routing result directly, so a
// reaction that stops being delivered fails even if the table still looks fine.
[TestClass]
public sealed class UiInputRouterTests
{
    private static readonly UiInputAction[] ChatEditingActions =
    [
        UiInputAction.Submit,
        UiInputAction.Cancel,
        UiInputAction.ChannelPrevious,
        UiInputAction.ChannelNext,
        UiInputAction.HistoryUp,
        UiInputAction.HistoryDown,
        UiInputAction.CompletionUp,
        UiInputAction.CompletionDown,
        UiInputAction.CaretLeft,
        UiInputAction.CaretRight,
        UiInputAction.CompletionAccept,
        UiInputAction.Paste,
    ];

    private static readonly UiInputAction[] AllActions = Enum.GetValues<UiInputAction>();

    // a router with both consumers claiming everything, recording what actually fires
    private sealed class Harness
    {
        public readonly UiInputRouter Router = new();
        public readonly UiInputFrame Frame = new();
        public readonly List<UiInputAction> ChatFired = [];
        public readonly List<UiInputAction> PlayerListFired = [];
        public readonly UiInputRegistrations Chat;
        public readonly UiInputRegistrations PlayerList;

        private readonly HashSet<UiInputAction> chatHeld = [];
        private readonly HashSet<UiInputAction> playerListHeld = [];

        public Harness(UiFocusOwner focus)
        {
            Router.SetFocus(focus);
            Chat = Router.Register(UiInputConsumer.Chat);
            PlayerList = Router.Register(UiInputConsumer.PlayerList);

            foreach (UiInputRule rule in UiInputRouter.Rules)
            {
                bool isChat = rule.Consumer == UiInputConsumer.Chat;
                UiInputRegistrations set = isChat ? Chat : PlayerList;
                List<UiInputAction> log = isChat ? ChatFired : PlayerListFired;

                set.On(rule.Action, () => log.Add(rule.Action));
                set.OwnHeld(rule.Action);
                (isChat ? chatHeld : playerListHeld).Add(rule.Action);
            }
        }

        // routes one action and reports which consumer it reached
        public (bool Chat, bool PlayerList) RouteOne(UiInputAction action, bool held = false)
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

    // ---------------------------------------------------------------- the table itself

    [TestMethod]
    public void Rules_CoverEveryActionExactlyOnce()
    {
        var duplicates = UiInputRouter.Rules
            .GroupBy(rule => rule.Action)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.HasCount(0, duplicates, "an action routed twice would reach two consumers");

        var missing = AllActions.Except(UiInputRouter.Rules.Select(rule => rule.Action)).ToArray();
        Assert.HasCount(0, missing, "every action needs a rule, or it is silently dropped");

        var unknown = UiInputRouter.Rules.Select(rule => rule.Action).Except(AllActions).ToArray();
        Assert.HasCount(0, unknown, "a rule for a non-existent action is dead weight");
    }

    [TestMethod]
    public void Rules_RejectAnEmptyFocusList()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new UiInputRule(UiInputAction.Submit, UiInputConsumer.Chat));
    }

    // ---------------------------------------------------------------- one consumer only

    [TestMethod]
    public void NoActionEverReachesTwoConsumers()
    {
        foreach (UiFocusOwner focus in Enum.GetValues<UiFocusOwner>())
        {
            var harness = new Harness(focus);
            foreach (UiInputAction action in AllActions)
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
        var harness = new Harness(UiFocusOwner.Chat);
        Assert.IsTrue(
            harness.RouteOne(UiInputAction.CompletionAccept).Chat,
            "the chat got the completion accept");

        // the list binding is the same physical key, so it shouldn't see it too
        (bool chat, bool playerList) = harness.RouteOne(UiInputAction.PlayerListToggle);
        Assert.IsFalse(playerList, "the list must not see Tab while chatting");
        Assert.IsFalse(chat, "and the chat does not treat it as a list toggle");
    }

    [TestMethod]
    public void Tab_OpensTheListOnlyFromTheNeutralState()
    {
        Assert.IsTrue(
            new Harness(UiFocusOwner.None).RouteOne(UiInputAction.PlayerListToggle).PlayerList,
            "neutral focus opens the list");

        Assert.IsTrue(
            new Harness(UiFocusOwner.PlayerList).RouteOne(UiInputAction.PlayerListToggle).PlayerList,
            "and it can be closed again from the list");
    }

    // ---------------------------------------------------------------- focus

    [TestMethod]
    public void Focus_IsExplicitAndReportedAsTheOldFlag()
    {
        var router = new UiInputRouter();
        Assert.AreEqual(UiFocusOwner.None, router.Focus);
        Assert.IsFalse(router.HasFocus, "no owner means no focus");

        router.SetFocus(UiFocusOwner.Chat);
        Assert.IsTrue(router.HasFocus);

        router.SetFocus(UiFocusOwner.PlayerList);
        Assert.IsTrue(router.HasFocus);
        Assert.AreEqual(UiFocusOwner.PlayerList, router.Focus);

        router.SetFocus(UiFocusOwner.None);
        Assert.IsFalse(router.HasFocus);
    }

    // ---------------------------------------------------------------- focus gating

    [TestMethod]
    public void OpenActions_ApplyOnlyFromTheNeutralState()
    {
        foreach (UiInputAction action in (UiInputAction[])[UiInputAction.ChatToggle, UiInputAction.ChatCommandToggle])
        {
            Assert.IsTrue(new Harness(UiFocusOwner.None).RouteOne(action).Chat, $"{action} from neutral");

            Assert.IsFalse(
                new Harness(UiFocusOwner.Chat).RouteOne(action).Chat,
                $"{action} is inert while chatting");
            Assert.IsFalse(
                new Harness(UiFocusOwner.PlayerList).RouteOne(action).Chat,
                $"{action} is inert while the list is open");
        }
    }

    [TestMethod]
    public void ChatEditingActions_RequireChatFocus()
    {
        foreach (UiInputAction action in ChatEditingActions)
        {
            Assert.IsTrue(
                new Harness(UiFocusOwner.Chat).RouteOne(action).Chat,
                $"{action} reaches the chat when it owns focus");

            Assert.IsFalse(
                new Harness(UiFocusOwner.None).RouteOne(action).Chat,
                $"{action} is inert with no focus");

            Assert.IsFalse(
                new Harness(UiFocusOwner.PlayerList).RouteOne(action).Chat,
                $"{action} is inert while the list is open");
        }
    }

    [TestMethod]
    public void PlayerListScroll_RequiresPlayerListFocus()
    {
        foreach (UiInputAction action in (UiInputAction[])[UiInputAction.PlayerListScrollUp, UiInputAction.PlayerListScrollDown])
        {
            Assert.IsTrue(
                new Harness(UiFocusOwner.PlayerList).RouteOne(action, held: true).PlayerList,
                $"{action} reaches the list when it owns focus");

            Assert.IsFalse(
                new Harness(UiFocusOwner.None).RouteOne(action, held: true).PlayerList,
                $"{action} is inert with no focus");
        }
    }

    [TestMethod]
    public void HeldPlayerListToggle_IsNotDeliveredWhileTheChatOwnsFocus()
    {
        Assert.IsTrue(
            new Harness(UiFocusOwner.None).RouteOne(UiInputAction.PlayerListToggle, held: true).PlayerList,
            "hold mode works from the neutral state");

        Assert.IsFalse(
            new Harness(UiFocusOwner.Chat).RouteOne(UiInputAction.PlayerListToggle, held: true).PlayerList,
            "and never while the chat owns the keyboard");
    }

    // ---------------------------------------------------------------- chat scroll

    [TestMethod]
    public void ChatScroll_IsOwnedByTheChatListEverywhereExceptThePlayerList()
    {
        static float Route(UiFocusOwner focus)
        {
            var router = new UiInputRouter();
            router.SetFocus(focus);
            router.Route(new UiInputFrame { ChatScrollDelta = 120f });
            return router.ChatScrollDelta;
        }

        Assert.AreEqual(120f, Route(UiFocusOwner.None), "scrolling works while walking");
        Assert.AreEqual(120f, Route(UiFocusOwner.Chat), "and while chatting");
        Assert.AreEqual(0f, Route(UiFocusOwner.PlayerList), "but not while the list owns the keyboard");
    }

    [TestMethod]
    public void ChatScrollDelta_DoesNotLeakBetweenFrames()
    {
        var router = new UiInputRouter();
        router.Route(new UiInputFrame { ChatScrollDelta = 50f });
        Assert.AreEqual(50f, router.ChatScrollDelta);

        router.Route(new UiInputFrame());
        Assert.AreEqual(0f, router.ChatScrollDelta, "the previous wheel delta is gone");
    }

    [TestMethod]
    public void ChatListPaging_IsDeliveredAsAHeldLevel()
    {
        var open = new Harness(UiFocusOwner.None);
        open.RouteOne(UiInputAction.ChatListScrollUp, held: true);
        Assert.IsTrue(open.Chat.IsHeld(UiInputAction.ChatListScrollUp), "PageUp is a held key");

        var blocked = new Harness(UiFocusOwner.PlayerList);
        blocked.RouteOne(UiInputAction.ChatListScrollUp, held: true);
        Assert.IsFalse(blocked.Chat.IsHeld(UiInputAction.ChatListScrollUp), "but not while the list is open");
    }

    // ---------------------------------------------------------------- routing is a pure function

    [TestMethod]
    public void Routing_IsAFunctionOfFrameAndFocusOnly()
    {
        // routing the same frame twice with the same focus has to give the same answer, regardless
        // of the caller: delivery depends only on the frame and the focus owner.
        var harness = new Harness(UiFocusOwner.None);
        harness.Frame.Press(UiInputAction.CompletionAccept);
        harness.Frame.Press(UiInputAction.PlayerListToggle);
        harness.Frame.Press(UiInputAction.ChatToggle);

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
        var router = new UiInputRouter();
        router.SetFocus(UiFocusOwner.Chat);

        UiInputRegistrations chat = router.Register(UiInputConsumer.Chat);
        var fired = new List<UiInputAction>();
        chat.On(UiInputAction.Cancel, () =>
        {
            fired.Add(UiInputAction.Cancel);
            router.SetFocus(UiFocusOwner.None);
        });
        chat.On(UiInputAction.Paste, () => fired.Add(UiInputAction.Paste));

        var frame = new UiInputFrame();
        frame.Press(UiInputAction.Cancel);
        frame.Press(UiInputAction.Paste);
        router.Route(frame);

        CollectionAssert.AreEqual(
            new[] { UiInputAction.Cancel },
            fired.ToArray(),
            "Cancel closed the box, so Paste must not have run");
    }

    // ---------------------------------------------------------------- registration contract

    [TestMethod]
    public void Register_IsIdempotentPerConsumer()
    {
        var router = new UiInputRouter();
        Assert.AreSame(router.Register(UiInputConsumer.Chat), router.Register(UiInputConsumer.Chat));
        Assert.AreNotSame(router.Register(UiInputConsumer.Chat), router.Register(UiInputConsumer.PlayerList));
    }

    [TestMethod]
    public void On_RejectsASecondReactionToTheSameAction()
    {
        var router = new UiInputRouter();
        UiInputRegistrations chat = router.Register(UiInputConsumer.Chat);
        chat.On(UiInputAction.Submit, () => { });

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.On(UiInputAction.Submit, () => { }));
        Assert.Contains("already claimed", error.Message);
    }

    [TestMethod]
    public void Claim_RejectsAnActionOwnedByAnotherConsumer()
    {
        var router = new UiInputRouter();
        UiInputRegistrations chat = router.Register(UiInputConsumer.Chat);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.On(UiInputAction.PlayerListToggle, () => { }));
        Assert.Contains("PlayerList", error.Message);
    }

    [TestMethod]
    public void IsHeld_RejectsAPollWithoutAClaim()
    {
        var router = new UiInputRouter();
        UiInputRegistrations chat = router.Register(UiInputConsumer.Chat);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.IsHeld(UiInputAction.ChatListScrollUp));
        Assert.Contains("OwnHeld", error.Message);
    }

    [TestMethod]
    public void Seal_AcceptsARouterWhereEverythingIsClaimed()
    {
        new Harness(UiFocusOwner.None).Router.Seal();
    }

    [TestMethod]
    public void Seal_RejectsAnActionNobodyClaimed()
    {
        // The bug this guards against: a rule the router happily routes but no component reads.
        var router = new UiInputRouter();
        router.Register(UiInputConsumer.Chat).On(UiInputAction.Submit, () => { });

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(router.Seal);
        Assert.Contains("nothing claimed it", error.Message);
        Assert.Contains(nameof(UiInputAction.Paste), error.Message, "the unclaimed action is named");
    }

    [TestMethod]
    public void Seal_RejectsRegistrationAfterItHasRun()
    {
        UiInputRouter router = new Harness(UiFocusOwner.None).Router;
        router.Seal();

        Assert.ThrowsExactly<InvalidOperationException>(() => router.Register(UiInputConsumer.Chat));
    }

    // ---------------------------------------------------------------- frame

    [TestMethod]
    public void Frame_ClearResetsEverything()
    {
        var frame = new UiInputFrame();
        frame.Press(UiInputAction.Submit);
        frame.Hold(UiInputAction.PlayerListToggle);
        frame.ChatScrollDelta = 10f;

        frame.Clear();

        Assert.HasCount(0, (IReadOnlyCollection<UiInputAction>)frame.Pressed);
        Assert.HasCount(0, (IReadOnlyCollection<UiInputAction>)frame.Held);
        Assert.AreEqual(0f, frame.ChatScrollDelta);
    }
}
