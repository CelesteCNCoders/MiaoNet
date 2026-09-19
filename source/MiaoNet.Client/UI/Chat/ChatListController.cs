using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.Chat;

namespace Celeste.Mod.MiaoNet.UI.Chat;

// scroll and fade state for the chat message list.
//
// scroll is measured from the bottom: 0 means the newest message sits at the
// baseline, and increasing it pulls older content down into view.
//
// the scroll clamp and the rendered content height both use the same item extent, derived from
// the ChatMessagePadding setting, so the scroll range matches the content for any setting and you
// can't scroll past the end.
public sealed class ChatListController
{
    private sealed class FadeState
    {
        public float ShowTimer;
        public float FadeOut = 1f;
        public float CounterPopTimer;
    }

    private readonly Dictionary<object, FadeState> states = [];
    private bool active;

    // whether the input box is open. while active, messages never fade out.
    //
    // closing it also resets the scroll position. this lives here instead of in the host so every
    // caller that closes the chat gets it, and so it can be tested without the game.
    public bool Active
    {
        get => active;
        set
        {
            if (active && !value)
            {
                ResetScroll();
            }

            active = value;
        }
    }

    // how long a message stays fully visible, from ChatDisplayDuration.
    public float ShowDuration { get; set; } = 8f;

    // height of one message row, including its vertical padding.
    public float ItemExtent { get; set; } = 1f;

    // visible height of the list, from the idle/active height ratio.
    public float ViewportHeight { get; set; }

    // number of messages in the displayed list, which drives the content height. pushed by
    // SetMessages and never synced by the host, so it can't end up describing a different list
    // than the one on screen.
    public int MessageCount { get; internal set; }

    // drives the rainbow cycle of the fold counter.
    public float CounterAnimClock { get; private set; }

    // smoothed scroll offset from the bottom.
    public float Scroll { get; private set; }

    public float ScrollTarget { get; private set; }

    public float ContentHeight => MessageCount * ItemExtent;

    // upper bound on scrolling; uses the same item extent as the content height.
    public float MaxScroll => MathF.Max(0f, ContentHeight - ViewportHeight);

    // scroll position from the top, which is what the virtual list uses.
    // not clamped on purpose: when the log is shorter than the viewport this goes negative so the
    // newest message stays pinned to the bottom. MaxScroll still clamps scrolling, so a short log
    // just can't scroll.
    public float ContentOffset => (ContentHeight - ViewportHeight) - Scroll;

    public bool IsScrolledBack => Scroll > 0.001f;

    public void Reset()
    {
        states.Clear();
        CounterAnimClock = 0f;
        ResetScroll();
    }

    // back to the newest message. closing the input box discards the scroll position instead of
    // leaving the list parked in history.
    public void ResetScroll()
    {
        Scroll = 0f;
        ScrollTarget = 0f;
    }

    // applies a wheel or page-key scroll delta; positive scrolls back into older content.
    public void ScrollBy(float delta)
    {
        ScrollTarget = Math.Clamp(ScrollTarget + delta, 0f, MaxScroll);
    }

    // advances the fade timers over the full log, not just the visible rows.
    public void UpdateTimers(IReadOnlyList<object> fullLogKeys, IReadOnlyList<int> repeatCounts, float deltaTime)
    {
        CounterAnimClock += deltaTime;

        for (int i = fullLogKeys.Count - 1; i >= 0; i--)
        {
            object key = fullLogKeys[i];

            if (!states.TryGetValue(key, out FadeState? state))
            {
                // a freshly folded line shows up with its counter pop playing.
                state = new FadeState
                {
                    ShowTimer = ShowDuration,
                    CounterPopTimer = repeatCounts[i] > 1 ? FoldCounter.PopDuration : 0f,
                };
                states[key] = state;
            }

            if (state.CounterPopTimer > 0f)
            {
                state.CounterPopTimer = MathF.Max(0f, state.CounterPopTimer - deltaTime);
            }

            if (state.ShowTimer > 0f)
            {
                state.ShowTimer -= deltaTime;
            }
            else if (state.FadeOut > 0f)
            {
                state.FadeOut -= (1f / ChatLayout.DisappearDuration) * deltaTime;
                if (state.FadeOut < 0f)
                {
                    state.FadeOut = 0f;
                }
            }
            else
            {
                // older messages are already invisible, nothing left to animate.
                break;
            }
        }

        if (states.Count > fullLogKeys.Count)
        {
            var live = new HashSet<object>(fullLogKeys);
            List<object>? stale = null;
            foreach (object key in states.Keys)
            {
                if (!live.Contains(key))
                {
                    (stale ??= []).Add(key);
                }
            }

            if (stale is not null)
            {
                foreach (object key in stale)
                {
                    states.Remove(key);
                }
            }
        }
    }

    public void UpdateScroll(float deltaTime)
    {
        ScrollTarget = Math.Clamp(ScrollTarget, 0f, MaxScroll);
        float maxMove = MathF.Max(MathF.Abs(ScrollTarget - Scroll), 8f) * 8f * deltaTime;
        Scroll = Scroll < ScrollTarget
            ? MathF.Min(Scroll + maxMove, ScrollTarget)
            : MathF.Max(Scroll - maxMove, ScrollTarget);
    }

    // fade factor of a message; forced fully opaque while the input box is open.
    public float FadeOf(object key)
    {
        if (Active)
        {
            return 1f;
        }

        return states.TryGetValue(key, out FadeState? state) ? state.FadeOut : 1f;
    }

    // 0 at the start of the counter pop animation, 1 when it finished.
    public float CounterPopProgressOf(object key)
        => states.TryGetValue(key, out FadeState? state)
            ? 1f - (state.CounterPopTimer / FoldCounter.PopDuration)
            : 1f;
}
