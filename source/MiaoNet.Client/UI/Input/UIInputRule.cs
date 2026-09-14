using System;

namespace Celeste.Mod.MiaoNet.UI.Input;

// one row of the routing table: which module an action belongs to, and which focus states it's live
// in.
//
// the focus list is the whole arbitration rule. no predicate form on purpose: an explicit list can
// be printed, diffed and checked exhaustively, so "one consumer per action" stays true by
// construction instead of by review.
public sealed class UIInputRule
{
    public UIInputRule(UIInputAction action, UIInputConsumer consumer, params UIFocusOwner[] focuses)
    {
        ArgumentNullException.ThrowIfNull(focuses);
        if (focuses.Length == 0)
        {
            throw new ArgumentException($"{action} must be live in at least one focus state", nameof(focuses));
        }

        Action = action;
        Consumer = consumer;
        Focuses = focuses;
    }

    public UIInputAction Action { get; }

    public UIInputConsumer Consumer { get; }

    // the focus states this action is live in; any other focus drops it.
    public UIFocusOwner[] Focuses { get; }

    public bool Applies(UIFocusOwner focus)
    {
        foreach (UIFocusOwner allowed in Focuses)
        {
            if (allowed == focus)
            {
                return true;
            }
        }

        return false;
    }
}
