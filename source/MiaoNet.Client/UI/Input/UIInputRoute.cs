using System;

namespace Celeste.Mod.MiaoNet.UI.Input;

// how an action is consumed. flags because one action can be both: the player list toggle is an edge
// in press mode and a level in hold mode.
[Flags]
public enum UIInputPhase
{
    None = 0,

    Press = 1 << 0,

    Hold = 1 << 1,
}

// one row of the routing table: who owns the action, how they consume it, and the focus states it is
// live in.
//
// ownership is written down here and nowhere else. that is what makes "one consumer per action" a
// property of the table instead of something a startup check has to keep honest.
public sealed class UIInputRoute
{
    public UIInputRoute(UIInputScope owner, UIInputPhase phase, params UIInputScope[] lives)
    {
        ArgumentNullException.ThrowIfNull(lives);
        if (owner == UIInputScope.Neutral)
        {
            throw new ArgumentException("nothing can be owned by the neutral scope", nameof(owner));
        }

        if (lives.Length == 0)
        {
            throw new ArgumentException("a route must be live in at least one focus state", nameof(lives));
        }

        Owner = owner;
        Phase = phase;
        Lives = lives;
    }

    public UIInputScope Owner { get; }

    public UIInputPhase Phase { get; }

    // the focus states this action is live in; any other focus drops it.
    public UIInputScope[] Lives { get; }

    public bool Applies(UIInputScope focus)
    {
        foreach (UIInputScope allowed in Lives)
        {
            if (allowed == focus)
            {
                return true;
            }
        }

        return false;
    }
}
