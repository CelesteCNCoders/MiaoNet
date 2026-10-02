using System;

namespace Celeste.Mod.MiaoNet.UI.Input;

// what one panel gets from the router: the actions the table assigns to its scope, and the reactions
// it registered on them.
//
// there is no claim step any more. the routing table already says who owns what, so asking about an
// action this scope doesn't own -- or consuming it in a phase it isn't routed for -- is a
// programming error and says so.
public sealed class UIInputHandle
{
    private readonly UIInputRouter router;

    internal UIInputHandle(UIInputRouter router, UIInputScope scope)
    {
        this.router = router;
        Scope = scope;
    }

    public UIInputScope Scope { get; }

    // registers a reaction to an edge. it fires when the routing table sends the action here and the
    // focus at the time allows it, so the focus condition lives in the table instead of a guard at the
    // call site.
    //
    // reactions run in registration order during Route(), before any component updates. one that takes
    // or releases the keyboard ends the frame's routing, so closing the chat box doesn't also submit
    // or page with the rest of the frame's keys.
    public UIInputHandle On(UIInputAction action, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Require(action, UIInputPhase.Press, "react to");
        router.AddReaction(action, handler);
        return this;
    }

    // whether this panel got the held level this frame. held input is a state rather than an event, so
    // it is polled instead of reacted to.
    public bool IsHeld(UIInputAction action)
    {
        Require(action, UIInputPhase.Hold, "poll the held level of");
        return router.IsDelivered(action, isPress: false);
    }

    private void Require(UIInputAction action, UIInputPhase phase, string what)
    {
        if (!UIInputRouter.Routes.TryGetValue(action, out UIInputRoute? route))
        {
            throw new InvalidOperationException($"{action} has no routing row; add one to UIInputRouter.Routes");
        }

        if (route.Owner != Scope)
        {
            throw new InvalidOperationException(
                $"{Scope} is trying to {what} {action}, which {route.Owner} owns");
        }

        if ((route.Phase & phase) == 0)
        {
            throw new InvalidOperationException($"{action} is not routed as {phase}");
        }
    }
}
