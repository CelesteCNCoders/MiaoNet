using System;
using System.Collections.Generic;

namespace Celeste.Mod.MiaoNet.UI.Input;

// the single arbitration point for UI input: it owns which ui has focus and, per frame, which
// consumer each action reaches.
//
// Routes is the whole arbitration rule, and the only place ownership is written down. consumers
// don't read it: they take the handle for their scope and react to what it delivers.
public sealed class UIInputRouter
{
    // one row per action, so an action can never reach two consumers.
    //
    // each row lists the focus states it is live in rather than a predicate on purpose: the list can
    // be enumerated and checked, and [Neutral, Chat] says "everything except the player list" more
    // honestly than a negated comparison would.
    public static readonly IReadOnlyDictionary<UIInputAction, UIInputRoute> Routes = BuildRoutes();

    private readonly Dictionary<UIInputScope, UIInputHandle> handles = [];
    private readonly List<Reaction> reactions = [];
    private readonly HashSet<UIInputAction> reacted = [];

    private readonly HashSet<UIInputAction> pressed = [];
    private readonly HashSet<UIInputAction> held = [];
    private readonly HashSet<UIInputAction> deliveredPressed = [];
    private readonly HashSet<UIInputAction> deliveredHeld = [];

    private float frameScrollDelta;

    // which ui owns the keyboard. set explicitly when a panel opens or closes.
    public UIInputScope Focus { get; private set; }

    // true while some ui owns the keyboard.
    public bool HasFocus => Focus != UIInputScope.Neutral;

    // wheel delta for the chat message list this frame, in pixel units. zero when the player list owns
    // the keyboard, so the delta is dropped instead of queued.
    public float ChatScrollDelta { get; private set; }

    public void SetFocus(UIInputScope scope) => Focus = scope;

    // the handle for one scope. idempotent: every class of the same panel gets the same handle, which
    // is how the chat's input box and its message list share one arbitration slot.
    public UIInputHandle Handle(UIInputScope scope)
    {
        if (scope == UIInputScope.Neutral)
        {
            throw new ArgumentException("the neutral scope has no handle", nameof(scope));
        }

        if (!handles.TryGetValue(scope, out UIInputHandle? handle))
        {
            handle = new UIInputHandle(this, scope);
            handles.Add(scope, handle);
        }

        return handle;
    }

    // one frame of input, in three steps: BeginFrame, then Press/Hold/SetChatScrollDelta from the
    // adapter, then Route.
    public void BeginFrame()
    {
        pressed.Clear();
        held.Clear();
        deliveredPressed.Clear();
        deliveredHeld.Clear();
        frameScrollDelta = 0f;
    }

    public void Press(UIInputAction action) => pressed.Add(action);

    public void Hold(UIInputAction action) => held.Add(action);

    public void SetChatScrollDelta(float delta) => frameScrollDelta = delta;

    // arbitrates one frame. reactions run here once before any component updates, so priority does
    // not depend on the order of the components.
    public void Route()
    {
        // the frame is arbitrated for the focus it started with. a reaction that moves the focus
        // invalidates the rest of the frame.
        UIInputScope routedFocus = Focus;

        ChatScrollDelta = Applies(UIInputAction.ChatListScrollUp, routedFocus) ? frameScrollDelta : 0f;

        foreach (UIInputAction action in pressed)
        {
            Deliver(action, isPress: true, routedFocus);
        }

        foreach (UIInputAction action in held)
        {
            Deliver(action, isPress: false, routedFocus);
        }

        foreach (Reaction reaction in reactions)
        {
            if (!deliveredPressed.Contains(reaction.Action))
            {
                continue;
            }

            reaction.Handler();

            if (Focus != routedFocus)
            {
                // the keyboard changed hands, so the rest were routed for an owner that's gone now.
                // closing the chat box shouldn't also submit or page.
                break;
            }
        }
    }

    internal void AddReaction(UIInputAction action, Action handler)
    {
        if (!reacted.Add(action))
        {
            throw new InvalidOperationException($"{action} already has a reaction");
        }

        reactions.Add(new Reaction(action, handler));
    }

    internal bool IsDelivered(UIInputAction action, bool isPress)
        => isPress ? deliveredPressed.Contains(action) : deliveredHeld.Contains(action);

    private static bool Applies(UIInputAction action, UIInputScope focus)
        => Routes.TryGetValue(action, out UIInputRoute? route) && route.Applies(focus);

    private void Deliver(UIInputAction action, bool isPress, UIInputScope routedFocus)
    {
        if (!Applies(action, routedFocus))
        {
            return;
        }

        if (isPress)
        {
            deliveredPressed.Add(action);
        }
        else
        {
            deliveredHeld.Add(action);
        }
    }

    private static Dictionary<UIInputAction, UIInputRoute> BuildRoutes()
    {
        var routes = new Dictionary<UIInputAction, UIInputRoute>
        {
            // --- opening the chat: only from the neutral state -------------------------------
            // whether the scene allows opening at all is still the consumer's call through
            // IsSuitableToOpenUI; this table only settles focus.
            [UIInputAction.ChatToggle] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Neutral),
            [UIInputAction.ChatCommandToggle] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Neutral),

            // --- toggling the player list: never while the chat owns the keyboard -------------
            // Tab is also the completion key, and the chat wins whenever it's open.
            [UIInputAction.PlayerListToggle] = new(
                UIInputScope.PlayerList,
                UIInputPhase.Press | UIInputPhase.Hold,
                UIInputScope.Neutral,
                UIInputScope.PlayerList),

            // --- chat editing: chat focus required -------------------------------------------
            [UIInputAction.Submit] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.Cancel] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.ChannelPrevious] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.ChannelNext] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.HistoryUp] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.HistoryDown] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.CompletionUp] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.CompletionDown] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.CaretLeft] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.CaretRight] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.CompletionAccept] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),
            [UIInputAction.Paste] = new(UIInputScope.Chat, UIInputPhase.Press, UIInputScope.Chat),

            // --- paging the chat message list: no focus of its own ---------------------------
            // it works while walking, and only the player list excludes it.
            [UIInputAction.ChatListScrollUp] = new(
                UIInputScope.Chat,
                UIInputPhase.Hold,
                UIInputScope.Neutral,
                UIInputScope.Chat),
            [UIInputAction.ChatListScrollDown] = new(
                UIInputScope.Chat,
                UIInputPhase.Hold,
                UIInputScope.Neutral,
                UIInputScope.Chat),

            // --- scrolling the player list: only while it is open ----------------------------
            [UIInputAction.PlayerListScrollUp] = new(
                UIInputScope.PlayerList,
                UIInputPhase.Hold,
                UIInputScope.PlayerList),
            [UIInputAction.PlayerListScrollDown] = new(
                UIInputScope.PlayerList,
                UIInputPhase.Hold,
                UIInputScope.PlayerList),
        };

        // the table is the only place that can say who owns an action, so a missing row is a key that
        // silently does nothing. catch it when the type is first touched instead.
        foreach (UIInputAction action in Enum.GetValues<UIInputAction>())
        {
            if (!routes.ContainsKey(action))
            {
                throw new InvalidOperationException($"{action} has no routing row");
            }
        }

        return routes;
    }

    private readonly record struct Reaction(UIInputAction Action, Action Handler);
}
