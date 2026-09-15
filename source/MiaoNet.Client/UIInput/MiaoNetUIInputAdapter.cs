using System;
using Celeste.Mod.MiaoNet.UI.Input;
using Microsoft.Xna.Framework.Input;

// Not "Celeste.Mod.MiaoNet.Input": that would shadow Celeste.Input (Gamepad/Jump/Rumble).
namespace Celeste.Mod.MiaoNet.UIInput;

// polls the game's input once per frame and turns it into UIInputActions. with UIInputRouter
// this is the only place in the client that reads UI input; components consume the arbitrated
// result.
//
// Poll is deliberately a flat list of "this input drives this action" lines. the four ways an
// input can be read -- a settings binding as an edge or as a level, a raw key as an edge or as a
// level, and a repeating button -- each live in exactly one helper below.
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
        bool shift = ShiftHeld();

        // settings bindings. a press is consumed unconditionally, so a binding the scene refuses to
        // act on still never leaks through to the game.
        Press(settings.ChatButton, UIInputAction.ChatToggle);
        Press(settings.ChatCommandButton, UIInputAction.ChatCommandToggle);
        Press(settings.PlayerListButton, UIInputAction.PlayerListToggle);
        Hold(settings.PlayerListButton, UIInputAction.PlayerListToggle);
        Hold(settings.PlayerListScrollUp, UIInputAction.PlayerListScrollUp);
        Hold(settings.PlayerListScrollDown, UIInputAction.PlayerListScrollDown);

        // plain keys. edges only, so holding them does not repeat.
        Press(Keys.Escape, UIInputAction.Cancel);
        Press(Keys.Enter, UIInputAction.Submit);
        Press(Keys.Tab, UIInputAction.CompletionAccept);
        Press(Keys.V, UIInputAction.Paste, ControlHeld());

        // Up/Down do double duty: the repeating buttons drive the completion popup while it is up,
        // and the raw edges drive the input history otherwise. the chat picks one by whether a
        // popup exists.
        Repeat(completionUp, UIInputAction.CompletionUp);
        Repeat(completionDown, UIInputAction.CompletionDown);
        Press(Keys.Up, UIInputAction.HistoryUp);
        Press(Keys.Down, UIInputAction.HistoryDown);

        // shift turns the horizontal keys into a channel switch instead of moving the caret.
        Press(Keys.Left, UIInputAction.ChannelPrevious, shift);
        Press(Keys.Right, UIInputAction.ChannelNext, shift);
        Repeat(caretLeft, UIInputAction.CaretLeft, when: !shift);
        Repeat(caretRight, UIInputAction.CaretRight, when: !shift);

        // levels: held while down. when both are held the chat list gives PageUp precedence.
        Hold(Keys.PageUp, UIInputAction.ChatListScrollUp);
        Hold(Keys.PageDown, UIInputAction.ChatListScrollDown);

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

    // a key from the settings. always consumes the press, even when nothing ends up acting on it.
    private void Press(ButtonBinding? binding, UIInputAction action)
    {
        if (binding is null || !binding.Pressed)
        {
            return;
        }

        binding.ConsumePress();
        frame.Press(action);
    }

    // a settings key held down.
    private void Hold(ButtonBinding? binding, UIInputAction action)
    {
        if (binding is not null && binding.Check)
        {
            frame.Hold(action);
        }
    }

    private void Press(Keys key, UIInputAction action, bool when = true)
    {
        if (when && MInput.Keyboard.Pressed(key))
        {
            frame.Press(action);
        }
    }

    private void Hold(Keys key, UIInputAction action)
    {
        if (MInput.Keyboard.Check(key))
        {
            frame.Hold(action);
        }
    }

    // a virtual button that repeats while held.
    private void Repeat(VirtualButton button, UIInputAction action, bool when = true)
    {
        if (!when || !button.Pressed)
        {
            return;
        }

        button.ConsumePress();
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

    private static bool ShiftHeld()
        => MInput.Keyboard.Check(Keys.LeftShift) || MInput.Keyboard.Check(Keys.RightShift);

    private static bool ControlHeld()
        => MInput.Keyboard.Check(Keys.LeftControl) || MInput.Keyboard.Check(Keys.RightControl);

    private static VirtualButton CreateRepeatButton(Keys key)
    {
        var button = new VirtualButton(new Binding() { Keyboard = [key] }, Input.Gamepad, 0f, TriggerThreshold);
        button.SetRepeat(RepeatDelay, RepeatInterval);
        return button;
    }
}
