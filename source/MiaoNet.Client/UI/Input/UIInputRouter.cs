using System;
using System.Collections.Generic;
using System.Text;

namespace Celeste.Mod.MiaoNet.UI.Input;

// the single arbitration point for UI input: it owns which UI has focus and, per frame, decides
// which consumer each action belongs to.
//
// Rules is the one place that says who gets what. consumers don't read it, they claim the actions
// it gives them through Register() and register a reaction, so the rule and the code that relies
// on it can't drift apart. Seal() refuses to let the process start if they do.
public sealed class UIInputRouter
{
    // the whole routing table. one row per action, so an action can never reach two consumers.
    //
    // each row is a focus list rather than a predicate on purpose: the list can be enumerated and
    // checked, and [None, Chat] says "everything except the player list" more honestly than a
    // negated comparison would.
    public static readonly IReadOnlyList<UIInputRule> Rules =
    [
        // --- opening the chat: only from the neutral state -------------------------------
        // whether the scene allows opening at all is still the consumer's call through
        // IsSuitableToOpenUI; this table only settles focus.
        new(UIInputAction.ChatToggle, UIInputConsumer.Chat, UIFocusOwner.None),
        new(UIInputAction.ChatCommandToggle, UIInputConsumer.Chat, UIFocusOwner.None),

        // --- toggling the player list: never while the chat owns the keyboard -------------
        // Tab is also the completion key, and the chat wins whenever it's open.
        new(
            UIInputAction.PlayerListToggle,
            UIInputConsumer.PlayerList,
            UIFocusOwner.None,
            UIFocusOwner.PlayerList),

        // --- chat editing: chat focus required -------------------------------------------
        new(UIInputAction.Submit, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.Cancel, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.ChannelPrevious, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.ChannelNext, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.HistoryUp, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.HistoryDown, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.CompletionUp, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.CompletionDown, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.CaretLeft, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.CaretRight, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.CompletionAccept, UIInputConsumer.Chat, UIFocusOwner.Chat),
        new(UIInputAction.Paste, UIInputConsumer.Chat, UIFocusOwner.Chat),

        // --- paging the chat message list: no focus of its own ---------------------------
        // it works while walking and only the player list excludes it.
        new(UIInputAction.ChatListScrollUp, UIInputConsumer.Chat, UIFocusOwner.None, UIFocusOwner.Chat),
        new(UIInputAction.ChatListScrollDown, UIInputConsumer.Chat, UIFocusOwner.None, UIFocusOwner.Chat),

        // --- scrolling the player list: only while it is open ----------------------------
        new(UIInputAction.PlayerListScrollUp, UIInputConsumer.PlayerList, UIFocusOwner.PlayerList),
        new(UIInputAction.PlayerListScrollDown, UIInputConsumer.PlayerList, UIFocusOwner.PlayerList),
    ];

    private static readonly Dictionary<UIInputAction, UIInputRule> RulesByAction = BuildIndex();

    private readonly Dictionary<UIInputConsumer, UIInputRegistrations> registrations = [];
    private readonly List<Reaction> reactions = [];

    private UIFocusOwner focus;
    private bool isSealed;

    // which UI owns the keyboard. set explicitly when a panel opens or closes.
    public UIFocusOwner Focus => focus;

    // true while some UI owns the keyboard.
    public bool HasFocus => focus != UIFocusOwner.None;

    // wheel delta for the chat message list this frame, in pixel units. zero when
    // the player list owns the keyboard, so the delta is dropped instead of queued.
    public float ChatScrollDelta { get; private set; }

    public void SetFocus(UIFocusOwner owner) => focus = owner;

    // the handle for one consumer's claims. idempotent: every class of the same consumer gets the
    // same handle, which is how the chat's input box and its message list share one arbitration
    // slot.
    public UIInputRegistrations Register(UIInputConsumer consumer)
    {
        if (isSealed)
        {
            throw new InvalidOperationException(
                $"cannot register {consumer} input after Seal(); register during component construction");
        }

        if (!registrations.TryGetValue(consumer, out UIInputRegistrations? set))
        {
            set = new UIInputRegistrations(this, consumer);
            registrations.Add(consumer, set);
        }

        return set;
    }

    // checks the routing table against what the consumers actually claimed. call once, after all
    // components are constructed.
    //
    // this turns "a rule nobody consumes" from a silently dead key into a startup failure.
    public void Seal()
    {
        if (isSealed)
        {
            return;
        }

        List<string>? problems = null;
        foreach (UIInputRule rule in Rules)
        {
            if (!registrations.TryGetValue(rule.Consumer, out UIInputRegistrations? set) || !set.Owns(rule.Action))
            {
                (problems ??= []).Add($"{rule.Action} is routed to {rule.Consumer}, but nothing claimed it");
            }
        }

        foreach (UIInputRegistrations set in registrations.Values)
        {
            foreach (UIInputAction action in set.Owned)
            {
                if (RuleFor(action) is not { } rule || rule.Consumer != set.Consumer)
                {
                    (problems ??= []).Add($"{set.Consumer} claimed {action}, which the routing table does not give it");
                }
            }
        }

        if (problems is not null)
        {
            throw new InvalidOperationException(
                "UI input routing table and its consumers disagree:\n  " + string.Join("\n  ", problems));
        }

        isSealed = true;
    }

    // arbitrates one frame of input. every action ends up with at most one consumer, and reactions
    // run here once before any component updates, so priority does not depend on the order of
    // MiaoNetContext.components.
    public void Route(UIInputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        foreach (UIInputRegistrations set in registrations.Values)
        {
            set.BeginFrame();
        }

        // the frame is arbitrated for the focus it started with. a reaction that moves the focus
        // invalidates the rest of the frame.
        UIFocusOwner routedFocus = focus;

        ChatScrollDelta = Applies(UIInputAction.ChatListScrollUp, routedFocus) ? frame.ChatScrollDelta : 0f;

        foreach (UIInputAction action in frame.Pressed)
        {
            Deliver(action, pressed: true, routedFocus);
        }

        foreach (UIInputAction action in frame.Held)
        {
            Deliver(action, pressed: false, routedFocus);
        }

        foreach (Reaction reaction in reactions)
        {
            if (!reaction.Owner.IsDeliveredPressed(reaction.Action))
            {
                continue;
            }

            reaction.Handler();

            if (focus != routedFocus)
            {
                // the keyboard changed hands, so the rest were routed for an owner that's gone now.
                // closing the chat box shouldn't also submit or page.
                break;
            }
        }
    }

    internal UIInputRule? RuleFor(UIInputAction action)
        => RulesByAction.GetValueOrDefault(action);

    internal void AddReaction(UIInputRegistrations owner, UIInputAction action, Action handler)
        => reactions.Add(new Reaction(owner, action, handler));

    private static bool Applies(UIInputAction action, UIFocusOwner focus)
        => RulesByAction.TryGetValue(action, out UIInputRule? rule) && rule.Applies(focus);

    private void Deliver(UIInputAction action, bool pressed, UIFocusOwner routedFocus)
    {
        if (!RulesByAction.TryGetValue(action, out UIInputRule? rule) || !rule.Applies(routedFocus))
        {
            return;
        }

        if (registrations.TryGetValue(rule.Consumer, out UIInputRegistrations? set))
        {
            set.Deliver(action, pressed);
        }
    }

    // indexes Rules, rejecting a duplicate action so one action can't reach two consumers.
    private static Dictionary<UIInputAction, UIInputRule> BuildIndex()
    {
        Dictionary<UIInputAction, UIInputRule> index = [];
        StringBuilder? duplicates = null;
        foreach (UIInputRule rule in Rules)
        {
            if (!index.TryAdd(rule.Action, rule))
            {
                (duplicates ??= new StringBuilder()).AppendLine(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"  {rule.Action}: {index[rule.Action].Consumer} and {rule.Consumer}");
            }
        }

        if (duplicates is not null)
        {
            throw new InvalidOperationException(
                "an action cannot be routed to two consumers:\n" + duplicates);
        }

        return index;
    }

    private readonly record struct Reaction(UIInputRegistrations Owner, UIInputAction Action, Action Handler);
}
