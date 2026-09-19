using System;

namespace Celeste.Mod.MiaoNet.UI.Input;

// one row of the routing table: an action and the focus states it's live in.
//
// who owns the action is not here on purpose. the consumer that registers it declares that, so
// ownership has exactly one source; this table only answers "is it live right now".
//
// the focus list is the whole arbitration rule. no predicate form on purpose: an explicit list can
// be printed, diffed and checked exhaustively.
public sealed class UIInputRule
{
    public UIInputRule(UIInputAction action, params UIFocusOwner[] focuses)
    {
        ArgumentNullException.ThrowIfNull(focuses);
        if (focuses.Length == 0)
        {
            throw new ArgumentException($"{action} must be live in at least one focus state", nameof(focuses));
        }

        Action = action;
        Focuses = focuses;
    }

    public UIInputAction Action { get; }

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
