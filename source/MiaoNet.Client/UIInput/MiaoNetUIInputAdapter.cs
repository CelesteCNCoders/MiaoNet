using System;
using Celeste.Mod.MiaoNet.UI.Input;
using Microsoft.Xna.Framework.Input;

// Not "Celeste.Mod.MiaoNet.Input": that would shadow Celeste.Input (Gamepad/Jump/Rumble).
namespace Celeste.Mod.MiaoNet.UIInput;

// polls the game's input once per frame and turns it into UiInputActions. with UIInputRouter
// this is the only place in the client that reads UI input; components consume the arbitrated
// result.
//
// two easy-to-miss details:
//   - caret movement and completion navigation use repeating virtual buttons, while input history
//     uses the raw edge, so holding Up doesn't scroll history;
//   - opening bindings are consumed unconditionally, even when the scene would refuse to open the
//     UI.
public sealed class MiaoNetUIInputAdapter : IDisposable
{
    // analogue trigger threshold.
    private const float TriggerThreshold = 0.4f;

    // delay before a held caret/completion button starts repeating.
    private const float RepeatDelay = 0.4f;

    // interval between repeats of a held caret/completion button.
    private const float RepeatInterval = 0.05f;

    private readonly UIInputRouter router;
    private readonly UIInputFrame frame = new();

    private readonly VirtualButton caretLeft;
    private readonly VirtualButton caretRight;
    private readonly VirtualButton completionUp;
    private readonly VirtualButton completionDown;

    private float lastWheelValue = float.NaN;
    private bool disposed;

    public MiaoNetUIInputAdapter(UIInputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        this.router = router;

        caretLeft = CreateRepeatButton(Keys.Left);
        caretRight = CreateRepeatButton(Keys.Right);
        completionUp = CreateRepeatButton(Keys.Up);
        completionDown = CreateRepeatButton(Keys.Down);
    }

    // reads this frame's input and arbitrates it. call once per frame, before components update.
    public void Poll()
    {
        frame.Clear();
        MiaoNetModuleSettings settings = MiaoNetModule.Settings;

        // opening bindings: consumed unconditionally.
        PressBinding(settings.ChatButton, UIInputAction.ChatToggle);
        PressBinding(settings.ChatCommandButton, UIInputAction.ChatCommandToggle);

        if (settings.PlayerListButton.Pressed)
        {
            settings.PlayerListButton.ConsumePress();
            frame.Press(UIInputAction.PlayerListToggle);
        }

        if (settings.PlayerListButton.Check)
        {
            frame.Hold(UIInputAction.PlayerListToggle);
        }

        // the player list's scroll keys are levels, not edges.
        if (settings.PlayerListScrollUp.Check)
        {
            frame.Hold(UIInputAction.PlayerListScrollUp);
        }

        if (settings.PlayerListScrollDown.Check)
        {
            frame.Hold(UIInputAction.PlayerListScrollDown);
        }

        if (MInput.Keyboard.Pressed(Keys.Escape))
        {
            frame.Press(UIInputAction.Cancel);
        }

        if (MInput.Keyboard.Pressed(Keys.Enter))
        {
            frame.Press(UIInputAction.Submit);
        }

        PollCaretOrChannelSwitch();

        // completion navigation repeats while held.
        if (completionUp.Pressed)
        {
            completionUp.ConsumePress();
            frame.Press(UIInputAction.CompletionUp);
        }
        else if (completionDown.Pressed)
        {
            completionDown.ConsumePress();
            frame.Press(UIInputAction.CompletionDown);
        }

        // input history is edge-only, so it reads the raw key instead of the repeating button.
        if (MInput.Keyboard.Pressed(Keys.Up))
        {
            frame.Press(UIInputAction.HistoryUp);
        }
        else if (MInput.Keyboard.Pressed(Keys.Down))
        {
            frame.Press(UIInputAction.HistoryDown);
        }

        if (MInput.Keyboard.Pressed(Keys.Tab))
        {
            frame.Press(UIInputAction.CompletionAccept);
        }

        if (MInput.Keyboard.Pressed(Keys.V) && ControlHeld())
        {
            frame.Press(UIInputAction.Paste);
        }

        // PageUp/PageDown are levels, with PageUp taking precedence when both are held.
        if (MInput.Keyboard.Check(Keys.PageUp))
        {
            frame.Hold(UIInputAction.ChatListScrollUp);
        }
        else if (MInput.Keyboard.Check(Keys.PageDown))
        {
            frame.Hold(UIInputAction.ChatListScrollDown);
        }

        frame.ChatScrollDelta = WheelDelta();

        router.Route(frame);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        caretLeft.Deregister();
        caretRight.Deregister();
        completionUp.Deregister();
        completionDown.Deregister();
    }

    // shift turns the horizontal keys into a channel switch; otherwise they move the caret.
    private void PollCaretOrChannelSwitch()
    {
        bool shift = MInput.Keyboard.CurrentState.IsKeyDown(Keys.LeftShift)
            || MInput.Keyboard.CurrentState.IsKeyDown(Keys.RightShift);

        if (shift)
        {
            if (MInput.Keyboard.Pressed(Keys.Left))
            {
                frame.Press(UIInputAction.ChannelPrevious);
            }
            else if (MInput.Keyboard.Pressed(Keys.Right))
            {
                frame.Press(UIInputAction.ChannelNext);
            }

            return;
        }

        if (caretLeft.Pressed)
        {
            caretLeft.ConsumePress();
            frame.Press(UIInputAction.CaretLeft);
        }
        else if (caretRight.Pressed)
        {
            caretRight.ConsumePress();
            frame.Press(UIInputAction.CaretRight);
        }
    }

    private void PressBinding(ButtonBinding? binding, UIInputAction action)
    {
        if (binding is null || !binding.Pressed)
        {
            return;
        }

        binding.ConsumePress();
        frame.Press(action);
    }

    // reads the wheel directly instead of MInput, which doesn't refresh the value in time.
    private float WheelDelta()
    {
        float wheel = Mouse.GetState().ScrollWheelValue;
        if (float.IsNaN(lastWheelValue))
        {
            lastWheelValue = wheel;
            return 0f;
        }

        float delta = wheel - lastWheelValue;
        lastWheelValue = wheel;
        return delta;
    }

    private static bool ControlHeld()
        => MInput.Keyboard.Check(Keys.LeftControl) || MInput.Keyboard.Check(Keys.RightControl);

    private static VirtualButton CreateRepeatButton(Keys key)
    {
        var button = new VirtualButton(new Binding() { Keyboard = [key] }, Input.Gamepad, 0f, TriggerThreshold);
        button.SetRepeat(RepeatDelay, RepeatInterval);
        return button;
    }
}
