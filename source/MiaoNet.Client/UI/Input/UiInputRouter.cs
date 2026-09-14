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
public sealed class UiInputRouter
{
    // the whole routing table. one row per action, so an action can never reach two consumers.
    //
    // each row is a focus list rather than a predicate on purpose: the list can be enumerated and
    // checked, and [None, Chat] says "everything except the player list" more honestly than a
    // negated comparison would.
    public static readonly IReadOnlyList<UiInputRule> Rules =
    [
        // --- opening the chat: only from the neutral state -------------------------------
        // whether the scene allows opening at all is still the consumer's call through
        // IsSuitableToOpenUI; this table only settles focus.
        new(UiInputAction.ChatToggle, UiInputConsumer.Chat, UiFocusOwner.None),
        new(UiInputAction.ChatCommandToggle, UiInputConsumer.Chat, UiFocusOwner.None),

        // --- toggling the player list: never while the chat owns the keyboard -------------
        // Tab is also the completion key, and the chat wins whenever it's open.
        new(
            UiInputAction.PlayerListToggle,
            UiInputConsumer.PlayerList,
            UiFocusOwner.None,
            UiFocusOwner.PlayerList),

        // --- chat editing: chat focus required -------------------------------------------
        new(UiInputAction.Submit, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.Cancel, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.ChannelPrevious, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.ChannelNext, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.HistoryUp, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.HistoryDown, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.CompletionUp, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.CompletionDown, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.CaretLeft, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.CaretRight, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.CompletionAccept, UiInputConsumer.Chat, UiFocusOwner.Chat),
        new(UiInputAction.Paste, UiInputConsumer.Chat, UiFocusOwner.Chat),

        // --- paging the chat message list: no focus of its own ---------------------------
        // it works while walking and only the player list excludes it.
        new(UiInputAction.ChatListScrollUp, UiInputConsumer.Chat, UiFocusOwner.None, UiFocusOwner.Chat),
        new(UiInputAction.ChatListScrollDown, UiInputConsumer.Chat, UiFocusOwner.None, UiFocusOwner.Chat),

        // --- scrolling the player list: only while it is open ----------------------------
        new(UiInputAction.PlayerListScrollUp, UiInputConsumer.PlayerList, UiFocusOwner.PlayerList),
        new(UiInputAction.PlayerListScrollDown, UiInputConsumer.PlayerList, UiFocusOwner.PlayerList),
    ];

    private static readonly Dictionary<UiInputAction, UiInputRule> RulesByAction = BuildIndex();

    private readonly Dictionary<UiInputConsumer, UiInputRegistrations> registrations = [];
    private readonly List<Reaction> reactions = [];

    private UiFocusOwner focus;
    private bool isSealed;

    // which UI owns the keyboard. set explicitly when a panel opens or closes.
    public UiFocusOwner Focus => focus;

    // true while some UI owns the keyboard.
    public bool HasFocus => focus != UiFocusOwner.None;

    // wheel delta for the chat message list this frame, in pixel units. zero when
    // the player list owns the keyboard, so the delta is dropped instead of queued.
    public float ChatScrollDelta { get; private set; }

    public void SetFocus(UiFocusOwner owner) => focus = owner;

    // the handle for one consumer's claims. idempotent: every class of the same consumer gets the
    // same handle, which is how the chat's input box and its message list share one arbitration
    // slot.
    public UiInputRegistrations Register(UiInputConsumer consumer)
    {
        if (isSealed)
        {
            throw new InvalidOperationException(
                $"cannot register {consumer} input after Seal(); register during component construction");
        }

        if (!registrations.TryGetValue(consumer, out UiInputRegistrations? set))
        {
            set = new UiInputRegistrations(this, consumer);
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
        foreach (UiInputRule rule in Rules)
        {
            if (!registrations.TryGetValue(rule.Consumer, out UiInputRegistrations? set) || !set.Owns(rule.Action))
            {
                (problems ??= []).Add($"{rule.Action} is routed to {rule.Consumer}, but nothing claimed it");
            }
        }

        foreach (UiInputRegistrations set in registrations.Values)
        {
            foreach (UiInputAction action in set.Owned)
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
    public void Route(UiInputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        foreach (UiInputRegistrations set in registrations.Values)
        {
            set.BeginFrame();
        }

        // the frame is arbitrated for the focus it started with. a reaction that moves the focus
        // invalidates the rest of the frame.
        UiFocusOwner routedFocus = focus;

        ChatScrollDelta = Applies(UiInputAction.ChatListScrollUp, routedFocus) ? frame.ChatScrollDelta : 0f;

        foreach (UiInputAction action in frame.Pressed)
        {
            Deliver(action, pressed: true, routedFocus);
        }

        foreach (UiInputAction action in frame.Held)
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

    internal UiInputRule? RuleFor(UiInputAction action)
        => RulesByAction.GetValueOrDefault(action);

    internal void AddReaction(UiInputRegistrations owner, UiInputAction action, Action handler)
        => reactions.Add(new Reaction(owner, action, handler));

    private static bool Applies(UiInputAction action, UiFocusOwner focus)
        => RulesByAction.TryGetValue(action, out UiInputRule? rule) && rule.Applies(focus);

    private void Deliver(UiInputAction action, bool pressed, UiFocusOwner routedFocus)
    {
        if (!RulesByAction.TryGetValue(action, out UiInputRule? rule) || !rule.Applies(routedFocus))
        {
            return;
        }

        if (registrations.TryGetValue(rule.Consumer, out UiInputRegistrations? set))
        {
            set.Deliver(action, pressed);
        }
    }

    // indexes Rules, rejecting a duplicate action so one action can't reach two consumers.
    private static Dictionary<UiInputAction, UiInputRule> BuildIndex()
    {
        Dictionary<UiInputAction, UiInputRule> index = [];
        StringBuilder? duplicates = null;
        foreach (UiInputRule rule in Rules)
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

    private readonly record struct Reaction(UiInputRegistrations Owner, UiInputAction Action, Action Handler);
}
