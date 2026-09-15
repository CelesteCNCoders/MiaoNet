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
        new(UIInputAction.ChatToggle, UIFocusOwner.None),
        new(UIInputAction.ChatCommandToggle, UIFocusOwner.None),

        // --- toggling the player list: never while the chat owns the keyboard -------------
        // Tab is also the completion key, and the chat wins whenever it's open.
        new(UIInputAction.PlayerListToggle, UIFocusOwner.None, UIFocusOwner.PlayerList),

        // --- chat editing: chat focus required -------------------------------------------
        new(UIInputAction.Submit, UIFocusOwner.Chat),
        new(UIInputAction.Cancel, UIFocusOwner.Chat),
        new(UIInputAction.ChannelPrevious, UIFocusOwner.Chat),
        new(UIInputAction.ChannelNext, UIFocusOwner.Chat),
        new(UIInputAction.HistoryUp, UIFocusOwner.Chat),
        new(UIInputAction.HistoryDown, UIFocusOwner.Chat),
        new(UIInputAction.CompletionUp, UIFocusOwner.Chat),
        new(UIInputAction.CompletionDown, UIFocusOwner.Chat),
        new(UIInputAction.CaretLeft, UIFocusOwner.Chat),
        new(UIInputAction.CaretRight, UIFocusOwner.Chat),
        new(UIInputAction.CompletionAccept, UIFocusOwner.Chat),
        new(UIInputAction.Paste, UIFocusOwner.Chat),

        // --- paging the chat message list: no focus of its own ---------------------------
        // it works while walking and only the player list excludes it.
        new(UIInputAction.ChatListScrollUp, UIFocusOwner.None, UIFocusOwner.Chat),
        new(UIInputAction.ChatListScrollDown, UIFocusOwner.None, UIFocusOwner.Chat),

        // --- scrolling the player list: only while it is open ----------------------------
        new(UIInputAction.PlayerListScrollUp, UIFocusOwner.PlayerList),
        new(UIInputAction.PlayerListScrollDown, UIFocusOwner.PlayerList),
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
        HashSet<UIInputAction> claimed = [];

        foreach (UIInputRule rule in Rules)
        {
            UIInputRegistrations? owner = OwnerOf(rule.Action);
            if (owner is null)
            {
                (problems ??= []).Add($"{rule.Action} is routed, but nothing claimed it");
                continue;
            }

            if (!claimed.Add(rule.Action))
            {
                (problems ??= []).Add($"{rule.Action} was claimed by more than one consumer");
            }
        }

        foreach (UIInputRegistrations set in registrations.Values)
        {
            foreach (UIInputAction action in set.Owned)
            {
                if (RuleFor(action) is null)
                {
                    (problems ??= []).Add($"{set.Consumer} claimed {action}, which has no routing rule");
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

    // who claimed this action. ownership is declared by the registration, not by the table, so this
    // is the one place that answers it.
    private UIInputRegistrations? OwnerOf(UIInputAction action)
    {
        foreach (UIInputRegistrations set in registrations.Values)
        {
            if (set.Owns(action))
            {
                return set;
            }
        }

        return null;
    }

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

        OwnerOf(action)?.Deliver(action, pressed);
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
                    $"  {rule.Action}");
            }
        }

        if (duplicates is not null)
        {
            throw new InvalidOperationException(
                "the routing table has duplicate rows:\n" + duplicates);
        }

        return index;
    }

    private readonly record struct Reaction(UIInputRegistrations Owner, UIInputAction Action, Action Handler);
}
