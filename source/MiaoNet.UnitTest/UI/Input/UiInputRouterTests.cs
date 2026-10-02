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

    // a router with a handle for each panel, recording what actually fires
    private sealed class Harness
    {
        public readonly UIInputRouter Router = new();
        public readonly List<UIInputAction> ChatFired = [];
        public readonly List<UIInputAction> PlayerListFired = [];
        public readonly UIInputHandle Chat;
        public readonly UIInputHandle PlayerList;

        public Harness(UIInputScope focus)
        {
            Router.SetFocus(focus);
            Chat = Router.Handle(UIInputScope.Chat);
            PlayerList = Router.Handle(UIInputScope.PlayerList);

            // a reaction for every edge the table gives this panel, so delivery is observed the way a
            // consumer observes it. held-only actions have no edge to watch.
            foreach ((UIInputAction action, UIInputRoute route) in UIInputRouter.Routes)
            {
                if ((route.Phase & UIInputPhase.Press) == 0)
                {
                    continue;
                }

                if (route.Owner == UIInputScope.Chat)
                {
                    Chat.On(action, () => ChatFired.Add(action));
                }
                else
                {
                    PlayerList.On(action, () => PlayerListFired.Add(action));
                }
            }
        }

        // routes one action and reports which panel it reached
        public (bool Chat, bool PlayerList) RouteOne(UIInputAction action, bool held = false)
        {
            ChatFired.Clear();
            PlayerListFired.Clear();

            Router.BeginFrame();
            if (held)
            {
                Router.Hold(action);
            }
            else
            {
                Router.Press(action);
            }

            Router.Route();

            if (!held)
            {
                return (ChatFired.Contains(action), PlayerListFired.Contains(action));
            }

            // a held level has no reaction to watch: only its owner can be asked about it
            UIInputScope owner = UIInputRouter.Routes[action].Owner;
            return (owner == UIInputScope.Chat && Chat.IsHeld(action),
                    owner == UIInputScope.PlayerList && PlayerList.IsHeld(action));
        }
    }

    // ---------------------------------------------------------------- the table itself

    [TestMethod]
    public void Routes_CoverEveryActionExactlyOnce()
    {
        var missing = AllActions.Except(UIInputRouter.Routes.Keys).ToArray();
        Assert.HasCount(0, missing, "every action needs a row, or it is silently dropped");

        var unknown = UIInputRouter.Routes.Keys.Except(AllActions).ToArray();
        Assert.HasCount(0, unknown, "a row for a non-existent action is dead weight");

        // one row per action is what makes "one consumer per action" structural, so every row has to
        // be complete on its own
        foreach ((UIInputAction action, UIInputRoute route) in UIInputRouter.Routes)
        {
            Assert.AreNotEqual(UIInputScope.Neutral, route.Owner, $"{action} needs a real owner");
            Assert.AreNotEqual(UIInputPhase.None, route.Phase, $"{action} must be consumed somehow");
            Assert.IsGreaterThan(0, route.Lives.Length, $"{action} must be live in some focus");
        }
    }

    [TestMethod]
    public void Routes_RejectAnImpossibleRow()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new UIInputRoute(UIInputScope.Chat, UIInputPhase.Press), "no live focus states");

        Assert.ThrowsExactly<ArgumentException>(
            () => new UIInputRoute(UIInputScope.Neutral, UIInputPhase.Press, UIInputScope.Neutral),
            "nothing can be owned by the neutral scope");
    }

    // ---------------------------------------------------------------- one consumer only

    [TestMethod]
    public void NoActionEverReachesTwoConsumers()
    {
        foreach (UIInputScope focus in Enum.GetValues<UIInputScope>())
        {
            var harness = new Harness(focus);
            foreach (UIInputAction action in AllActions)
            {
                (bool chat, bool playerList) = harness.RouteOne(action);
                Assert.IsFalse(chat && playerList, $"{action} reached both panels with focus {focus}");
            }
        }
    }

    // ---------------------------------------------------------------- tab / completion

    [TestMethod]
    public void Tab_GoesToCompletionWhenTheChatOwnsFocus()
    {
        var harness = new Harness(UIInputScope.Chat);
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
            new Harness(UIInputScope.Neutral).RouteOne(UIInputAction.PlayerListToggle).PlayerList,
            "neutral focus opens the list");

        Assert.IsTrue(
            new Harness(UIInputScope.PlayerList).RouteOne(UIInputAction.PlayerListToggle).PlayerList,
            "and it can be closed again from the list");
    }

    // ---------------------------------------------------------------- focus gating

    [TestMethod]
    public void OpenActions_ApplyOnlyFromTheNeutralState()
    {
        foreach (UIInputAction action in (UIInputAction[])[UIInputAction.ChatToggle, UIInputAction.ChatCommandToggle])
        {
            Assert.IsTrue(new Harness(UIInputScope.Neutral).RouteOne(action).Chat, $"{action} from neutral");

            Assert.IsFalse(
                new Harness(UIInputScope.Chat).RouteOne(action).Chat,
                $"{action} is inert while chatting");
            Assert.IsFalse(
                new Harness(UIInputScope.PlayerList).RouteOne(action).Chat,
                $"{action} is inert while the list is open");
        }
    }

    [TestMethod]
    public void ChatEditingActions_RequireChatFocus()
    {
        foreach (UIInputAction action in ChatEditingActions)
        {
            Assert.IsTrue(
                new Harness(UIInputScope.Chat).RouteOne(action).Chat,
                $"{action} reaches the chat when it owns focus");

            Assert.IsFalse(
                new Harness(UIInputScope.Neutral).RouteOne(action).Chat,
                $"{action} is inert with no focus");

            Assert.IsFalse(
                new Harness(UIInputScope.PlayerList).RouteOne(action).Chat,
                $"{action} is inert while the list is open");
        }
    }

    [TestMethod]
    public void PlayerListScroll_RequiresPlayerListFocus()
    {
        foreach (UIInputAction action in (UIInputAction[])[UIInputAction.PlayerListScrollUp, UIInputAction.PlayerListScrollDown])
        {
            Assert.IsTrue(
                new Harness(UIInputScope.PlayerList).RouteOne(action, held: true).PlayerList,
                $"{action} reaches the list when it owns focus");

            Assert.IsFalse(
                new Harness(UIInputScope.Neutral).RouteOne(action, held: true).PlayerList,
                $"{action} is inert with no focus");
        }
    }

    [TestMethod]
    public void HeldPlayerListToggle_IsNotDeliveredWhileTheChatOwnsFocus()
    {
        Assert.IsTrue(
            new Harness(UIInputScope.Neutral).RouteOne(UIInputAction.PlayerListToggle, held: true).PlayerList,
            "hold mode works from the neutral state");

        Assert.IsFalse(
            new Harness(UIInputScope.Chat).RouteOne(UIInputAction.PlayerListToggle, held: true).PlayerList,
            "and never while the chat owns the keyboard");
    }

    // ---------------------------------------------------------------- chat scroll

    [TestMethod]
    public void ChatScroll_IsOwnedByTheChatListEverywhereExceptThePlayerList()
    {
        static float Route(UIInputScope focus)
        {
            var router = new UIInputRouter();
            router.SetFocus(focus);
            router.BeginFrame();
            router.SetChatScrollDelta(120f);
            router.Route();
            return router.ChatScrollDelta;
        }

        Assert.AreEqual(120f, Route(UIInputScope.Neutral), "scrolling works while walking");
        Assert.AreEqual(120f, Route(UIInputScope.Chat), "and while chatting");
        Assert.AreEqual(0f, Route(UIInputScope.PlayerList), "but not while the list owns the keyboard");
    }

    [TestMethod]
    public void ChatScrollDelta_DoesNotLeakBetweenFrames()
    {
        var router = new UIInputRouter();
        router.BeginFrame();
        router.SetChatScrollDelta(50f);
        router.Route();
        Assert.AreEqual(50f, router.ChatScrollDelta);

        router.BeginFrame();
        router.Route();
        Assert.AreEqual(0f, router.ChatScrollDelta, "the previous wheel delta is gone");
    }

    [TestMethod]
    public void ChatListPaging_IsDeliveredAsAHeldLevel()
    {
        var open = new Harness(UIInputScope.Neutral);
        open.RouteOne(UIInputAction.ChatListScrollUp, held: true);
        Assert.IsTrue(open.Chat.IsHeld(UIInputAction.ChatListScrollUp), "PageUp is a held key");

        var blocked = new Harness(UIInputScope.PlayerList);
        blocked.RouteOne(UIInputAction.ChatListScrollUp, held: true);
        Assert.IsFalse(blocked.Chat.IsHeld(UIInputAction.ChatListScrollUp), "but not while the list is open");
    }

    // ---------------------------------------------------------------- routing is a pure function

    [TestMethod]
    public void Routing_IsAFunctionOfFrameAndFocusOnly()
    {
        // routing the same frame twice with the same focus has to give the same answer, regardless
        // of the caller: delivery depends only on the frame and the focus.
        var harness = new Harness(UIInputScope.Neutral);
        harness.Router.BeginFrame();
        harness.Router.Press(UIInputAction.CompletionAccept);
        harness.Router.Press(UIInputAction.PlayerListToggle);
        harness.Router.Press(UIInputAction.ChatToggle);

        harness.Router.Route();
        var first = (harness.ChatFired.ToArray(), harness.PlayerListFired.ToArray());

        harness.ChatFired.Clear();
        harness.PlayerListFired.Clear();
        harness.Router.Route();

        CollectionAssert.AreEqual(first.Item1, harness.ChatFired.ToArray(), "chat delivery is stable");
        CollectionAssert.AreEqual(first.Item2, harness.PlayerListFired.ToArray(), "list delivery is stable");
    }

    // ---------------------------------------------------------------- focus handover

    [TestMethod]
    public void Route_StopsAfterAReactionTakesOrReleasesTheKeyboard()
    {
        // a reaction that drops focus has to stop the rest of the frame acting on a closed panel.
        var router = new UIInputRouter();
        router.SetFocus(UIInputScope.Chat);

        UIInputHandle chat = router.Handle(UIInputScope.Chat);
        var fired = new List<UIInputAction>();
        chat.On(UIInputAction.Cancel, () =>
        {
            fired.Add(UIInputAction.Cancel);
            router.SetFocus(UIInputScope.Neutral);
        });
        chat.On(UIInputAction.Paste, () => fired.Add(UIInputAction.Paste));

        router.BeginFrame();
        router.Press(UIInputAction.Cancel);
        router.Press(UIInputAction.Paste);
        router.Route();

        CollectionAssert.AreEqual(
            new[] { UIInputAction.Cancel },
            fired.ToArray(),
            "Cancel closed the box, so Paste must not have run");
    }

    // ---------------------------------------------------------------- handle contract

    [TestMethod]
    public void Handle_IsIdempotentPerScope()
    {
        var router = new UIInputRouter();
        Assert.AreSame(router.Handle(UIInputScope.Chat), router.Handle(UIInputScope.Chat));
        Assert.AreNotSame(router.Handle(UIInputScope.Chat), router.Handle(UIInputScope.PlayerList));
        Assert.ThrowsExactly<ArgumentException>(
            () => router.Handle(UIInputScope.Neutral),
            "the neutral scope has nothing to handle input");
    }

    [TestMethod]
    public void On_RejectsASecondReactionToTheSameAction()
    {
        var router = new UIInputRouter();
        UIInputHandle chat = router.Handle(UIInputScope.Chat);
        chat.On(UIInputAction.Submit, () => { });

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.On(UIInputAction.Submit, () => { }));
        Assert.Contains("already has a reaction", error.Message);
    }

    [TestMethod]
    public void On_RejectsAnActionAnotherScopeOwns()
    {
        var router = new UIInputRouter();
        UIInputHandle chat = router.Handle(UIInputScope.Chat);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.On(UIInputAction.PlayerListToggle, () => { }));
        Assert.Contains("PlayerList owns", error.Message);
    }

    [TestMethod]
    public void IsHeld_RejectsAnActionThatIsNotAHeldLevel()
    {
        var router = new UIInputRouter();
        UIInputHandle chat = router.Handle(UIInputScope.Chat);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.IsHeld(UIInputAction.Submit));
        Assert.Contains("not routed as", error.Message);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => chat.IsHeld(UIInputAction.PlayerListScrollUp),
            "and one this scope doesn't own");
    }

}
