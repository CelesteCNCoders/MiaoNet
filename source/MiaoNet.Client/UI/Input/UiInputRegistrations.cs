using System;
using System.Collections.Generic;

namespace Celeste.Mod.MiaoNet.UI.Input;

// what one consumer registered: its reactions, and the actions it polls as held levels.
//
// a registration is per consumer, not per class. the chat's input box and its message list are
// different classes but the same consumer, and the routing table arbitrates by consumer, so they
// share the handle from UiInputRouter.Register().
//
// every action has to be claimed exactly once across all consumers. a rule with no claimant, a
// claim with no rule, and two claimants for one action are all rejected, so "one consumer per
// action" holds by construction instead of by re-reading the table.
public sealed class UiInputRegistrations
{
    private readonly UiInputRouter router;
    private readonly List<UiInputAction> owned = [];
    private readonly HashSet<UiInputAction> ownedPressed = [];
    private readonly HashSet<UiInputAction> ownedHeld = [];
    private readonly HashSet<UiInputAction> deliveredPressed = [];
    private readonly HashSet<UiInputAction> deliveredHeld = [];

    internal UiInputRegistrations(UiInputRouter router, UiInputConsumer consumer)
    {
        this.router = router;
        Consumer = consumer;
    }

    public UiInputConsumer Consumer { get; }

    // every action this consumer claimed, by reaction or by held polling.
    internal IReadOnlyList<UiInputAction> Owned => owned;

    // registers a reaction to an edge. it fires when the routing table sends the action here and
    // the focus at the time allows it, so the focus condition lives in the table instead of a guard
    // at the call site.
    //
    // reactions run in registration order during Route(), before any component updates. one that
    // takes or releases the keyboard ends the frame's routing, so closing the chat box doesn't also
    // submit or page with the rest of the frame's keys.
    public UiInputRegistrations On(UiInputAction action, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Claim(action, pressed: true);
        router.AddReaction(this, action, handler);
        return this;
    }

    // claims an action as a held level with no reaction. held input is a state, not an event: as a
    // reaction it would need per-frame reset bookkeeping to answer "is it still down", so the level
    // is polled through IsHeld() instead.
    public UiInputRegistrations OwnHeld(UiInputAction action)
    {
        Claim(action, pressed: false);
        return this;
    }

    // whether this consumer got the held level this frame. delivery already applied the routing
    // table's focus condition, so no extra guard is needed.
    public bool IsHeld(UiInputAction action)
    {
        if (!ownedHeld.Contains(action))
        {
            throw new InvalidOperationException(
                $"{Consumer} polled the held level of {action} without claiming it via OwnHeld");
        }

        return deliveredHeld.Contains(action);
    }

    internal bool Owns(UiInputAction action) => ownedPressed.Contains(action) || ownedHeld.Contains(action);

    internal bool IsDeliveredPressed(UiInputAction action) => deliveredPressed.Contains(action);

    internal void BeginFrame()
    {
        deliveredPressed.Clear();
        deliveredHeld.Clear();
    }

    internal void Deliver(UiInputAction action, bool pressed)
    {
        if (pressed)
        {
            deliveredPressed.Add(action);
        }
        else
        {
            deliveredHeld.Add(action);
        }
    }

    // claims an action, rejecting one that isn't ours or a phase we already claimed.
    private void Claim(UiInputAction action, bool pressed)
    {
        if (router.RuleFor(action) is not { } rule)
        {
            throw new InvalidOperationException($"{action} has no routing rule; add one to UiInputRouter.Rules");
        }

        if (rule.Consumer != Consumer)
        {
            throw new InvalidOperationException(
                $"{Consumer} cannot claim {action}: the routing table gives it to {rule.Consumer}");
        }

        // one action can be both an edge and a level (the player list toggle is), but not the same
        // phase twice.
        bool added = pressed ? ownedPressed.Add(action) : ownedHeld.Add(action);
        if (!added)
        {
            throw new InvalidOperationException(
                $"{Consumer} already claimed {action} as {(pressed ? "an edge" : "a held level")}");
        }

        if (!owned.Contains(action))
        {
            owned.Add(action);
        }
    }
}
