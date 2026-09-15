using System;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// scroll state and animation for the player list: the smoothing curve, the keyboard scroll
// speed and the floating paused-icon phase. also caps the top of the scroll so the content
// can't run past the end.
// no game types in here so the scroll math stays unit testable.
public sealed class PlayerListController
{
    public const float KeyboardScrollSpeed = 1024f;

    // amplitude of the paused icon's float
    public const float PausedIconRange = 4f;

    private const float MinStep = 8f;
    private const float StepFactor = 8f;

    private float pausedIconTimer;

    public bool IsOpen { get; private set; }

    // smoothed offset we actually apply to the content
    public float Scroll { get; private set; }

    // where the smoothing is heading
    public float ScrollTarget { get; private set; }

    // full content height, refreshed after each layout pass
    public float ContentHeight { get; set; }

    // viewport height, usually the screen height
    public float ViewportHeight { get; set; }

    public bool ScrollUpHeld { get; set; }

    public bool ScrollDownHeld { get; set; }

    // horizontal offset of the floating paused icon
    public float PausedIconOffset { get; private set; }

    // maximum scroll offset, so the content can't scroll past its end
    public float MaxScroll => MathF.Max(0f, ContentHeight - ViewportHeight);

    public void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;
        if (!open)
        {
            ScrollTarget = 0f;
            Scroll = 0f;
        }
    }

    // called on disconnect
    public void Reset()
    {
        Scroll = 0f;
        ScrollTarget = 0f;
        pausedIconTimer = 0f;
        PausedIconOffset = 0f;
    }

    public void Update(float deltaTime)
    {
        if (IsOpen)
        {
            if (ScrollUpHeld)
            {
                ScrollTarget -= KeyboardScrollSpeed * deltaTime;
            }
            else if (ScrollDownHeld)
            {
                ScrollTarget += KeyboardScrollSpeed * deltaTime;
            }
        }

        ScrollTarget = Math.Clamp(ScrollTarget, 0f, MaxScroll);

        float maxMove = MathF.Max(MathF.Abs(ScrollTarget - Scroll), MinStep) * StepFactor * deltaTime;
        Scroll = Approach(Scroll, ScrollTarget, maxMove);

        pausedIconTimer = WrapAngle(pausedIconTimer + (deltaTime * 2f));
        PausedIconOffset = MathF.Sin(pausedIconTimer) * PausedIconRange;
    }

    // call after a layout pass has changed ContentHeight
    public void ClampToContent()
    {
        ScrollTarget = Math.Clamp(ScrollTarget, 0f, MaxScroll);
        Scroll = Math.Clamp(Scroll, 0f, MaxScroll);
    }

    private static float Approach(float value, float target, float maxMove)
        => value > target
            ? MathF.Max(value - maxMove, target)
            : MathF.Min(value + maxMove, target);

    // same as Monocle's Calc.WrapAngle, without taking the dependency
    private static float WrapAngle(float angle)
    {
        angle = MathF.IEEERemainder(angle, MathF.Tau);
        if (angle <= -MathF.PI)
        {
            angle += MathF.Tau;
        }
        else if (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        return angle;
    }
}
